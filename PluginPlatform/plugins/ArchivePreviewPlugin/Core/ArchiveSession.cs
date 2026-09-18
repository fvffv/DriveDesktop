using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ArchivePreviewPlugin.Core;

/// <summary>已经建立索引的归档会话；所有归档操作串行运行于后台线程。</summary>
internal sealed class ArchiveSession : IDisposable
{
    private readonly HttpRangeStream _stream;
    private readonly IArchive _archive;
    private readonly IArchiveEntry[] _entries;
    private readonly string? _password;
    private ICSharpCode.SharpZipLib.Zip.ZipFile? _encryptedZip;
    internal ArchiveItem[] Items { get; }
    internal bool IsSolid { get { return _archive.IsSolid; } }
    internal long NetworkBytes { get { return _stream.NetworkBytes; } }
    internal long CacheHitBytes { get { return _stream.CacheHitBytes; } }
    internal long ArchiveSize { get { return _stream.Length; } }
    internal string Format { get { return _archive.Type == ArchiveType.SevenZip ? "7z" : _archive.Type.ToString().ToUpperInvariant(); } }

    /// <summary>保存索引和原始条目映射，重复名称仍有独立编号。</summary>
    private ArchiveSession(HttpRangeStream stream, IArchive archive, IArchiveEntry[] entries, string? password)
    {
        _stream = stream; _archive = archive; _entries = entries;
        _password = password;
        Items = entries.Select((entry, index) => new ArchiveItem(index, entry.Key ?? "", entry.IsDirectory,
            entry.Size, entry.CompressedSize, entry.LastModifiedTime, entry.IsEncrypted,
            GetUnsafeReason(entry))).ToArray();
    }

    /// <summary>只读取归档元数据，不提取正文；限制索引读取量和条目数量。</summary>
    internal static ArchiveSession Open(HttpRangeStream stream, string extension, string? password, CancellationToken token)
    {
        IArchive? archive = null;
        try
        {
            stream.OperationToken = token;
            stream.Position = 0;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions
            {
                LeaveStreamOpen = true, Password = string.IsNullOrEmpty(password) ? null : password,
                LookForHeader = false, ExtensionHint = extension.TrimStart('.'),
                ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(936) }
            });
            if (archive.Type is not (ArchiveType.Zip or ArchiveType.SevenZip or ArchiveType.Rar or ArchiveType.Tar))
                throw new NotSupportedException("当前支持 ZIP、7z、RAR 和未压缩的 TAR；TAR.GZ 等连续压缩格式无法只读取目录。");
            var entries = new List<IArchiveEntry>();
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entries.Count >= 100_000) throw new IOException("压缩包超过 100,000 个条目，已停止建立索引。");
                if ((entry.Key?.Length ?? 0) > 4096) throw new IOException("压缩包中存在过长的条目路径。");
                entries.Add(entry);
            }
            if (!archive.IsComplete) throw new IOException("这是缺少其他分卷的压缩包，请使用完整的单卷压缩包。");
            return new ArchiveSession(stream, archive, entries.ToArray(), password);
        }
        catch { archive?.Dispose(); stream.Dispose(); throw; }
    }

    /// <summary>将选定文件提取到下载目录；仅在完整写入并校验后改为最终文件名。</summary>
    internal string[] Extract(int[] indices, string destinationRoot, Action<ExtractionProgress> report, CancellationToken token)
    {
        _stream.OperationToken = token;
        _stream.NetworkLimit = long.MaxValue;
        // 按压缩包大小自动选择 Range 分段；固实归档再提高一档，减少定位压缩块时的往返次数。
        _stream.ActiveBlockSize = SelectBlockSize(_stream.Length, _archive.IsSolid);
        var selected = indices.Distinct().Order().ToArray();
        foreach (var index in selected)
        {
            if (index < 0 || index >= Items.Length) throw new ArgumentOutOfRangeException(nameof(indices));
            if (Items[index].IsDirectory || Items[index].BlockedReason.Length > 0) throw new IOException("所选文件无法提取：" + Items[index].BlockedReason);
        }
        var total = selected.Aggregate(0L, (sum, index) => checked(sum + Math.Max(0, Items[index].Size)));
        var results = new List<string>();
        long written = 0;
        // 固实 RAR 必须按归档顺序解码到目标条目；7z 使用其按压缩块定位的随机访问实现。
        if (_archive.Type == ArchiveType.Rar && _archive.IsSolid)
        {
            using var reader = _archive.ExtractAllEntries();
            var index = -1;
            var wanted = selected.ToHashSet();
            while (wanted.Count > 0 && reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested(); index++;
                if (!wanted.Remove(index)) continue;
                using var source = reader.OpenEntryStream();
                results.Add(WriteEntry(source, index, destinationRoot, ref written, total, report, token));
            }
            if (wanted.Count != 0) throw new IOException("未能在固实压缩包中找到所有选中的文件。");
        }
        else
        {
            foreach (var index in selected)
            {
                token.ThrowIfCancellationRequested();
                report(new ExtractionProgress(Items[index].Path, written, total, results.Count, selected.Length));
                using var source = OpenEntryStream(index);
                results.Add(WriteEntry(source, index, destinationRoot, ref written, total, report, token));
            }
        }
        return results.ToArray();
    }

    /// <summary>加密 ZIP 使用会验证 AES 认证码的解码流；其他格式使用 SharpCompress 的按需解码。</summary>
    private Stream OpenEntryStream(int index)
    {
        if (_archive.Type != ArchiveType.Zip || !Items[index].Encrypted) return _entries[index].OpenEntryStream();
        if (_encryptedZip is null)
        {
            _stream.Position = 0;
            _encryptedZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(_stream)
            {
                IsStreamOwner = false, Password = _password
            };
        }
        if (_encryptedZip.Count != Items.Length || _encryptedZip[index].Size != Items[index].Size)
            throw new IOException("加密 ZIP 的目录信息不一致，无法提取。");
        return _encryptedZip.GetInputStream(index);
    }

    /// <summary>流式写入单个文件，限制为归档声明大小并验证 ZIP 的 CRC32；不覆盖已有文件。</summary>
    private string WriteEntry(Stream source, int index, string root, ref long written, long total,
        Action<ExtractionProgress> report, CancellationToken token)
    {
        var item = Items[index];
        var target = SafeExtractionPath.Resolve(root, item.Path);
        var directory = Path.GetDirectoryName(target)!;
        SafeExtractionPath.EnsureDirectories(directory);
        var temporary = Path.Combine(directory, ".drive-extract-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            long current = 0;
            uint crc = uint.MaxValue;
            var buffer = new byte[64 * 1024];
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var count = source.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    current += count;
                    if (current > item.Size) throw new IOException("解压长度超过目录声明的文件大小，已停止。");
                    output.Write(buffer, 0, count);
                    if (_archive.Type == ArchiveType.Zip) crc = Crc32.Update(crc, buffer.AsSpan(0, count));
                    written += count;
                    report(new ExtractionProgress(item.Path, written, total, 0, 0));
                }
                if (current != item.Size) throw new IOException("文件内容不完整，未保存为最终文件。");
                // WinZip AES AE-2 的目录 CRC 固定为 0，由解码流的认证码校验内容。
                // 普通 ZIP 和保留 CRC 的 AE-1 仍显式核对 CRC32。
                if (_archive.Type == ArchiveType.Zip && (!item.Encrypted || _entries[index].Crc != 0) &&
                    ~crc != unchecked((uint)_entries[index].Crc))
                    throw new IOException("ZIP 文件校验失败，可能是密码不正确或压缩包损坏。");
                output.Flush(true);
            }
            // 解码流的结束和清理也属于校验；必须在最终文件可见之前完成。
            source.Dispose();
            token.ThrowIfCancellationRequested();
            // 重新检查目录连接；已有文件采用递增后缀保留，不自动覆盖。
            SafeExtractionPath.EnsureDirectories(directory);
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var final = suffix == 0 ? target : Path.Combine(directory, Path.GetFileNameWithoutExtension(target) + $" ({suffix})" + Path.GetExtension(target));
                try { File.Move(temporary, final, false); return final; }
                catch (IOException) when (File.Exists(final) || Directory.Exists(final)) { }
            }
            throw new IOException("下载目录中有过多同名文件。");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>识别不能安全落盘的条目，仍允许用户查看其名称。</summary>
    private static string GetUnsafeReason(IArchiveEntry entry)
    {
        if (entry.Archive.Type is ArchiveType.Zip or ArchiveType.SevenZip && ((entry.Attrib.GetValueOrDefault() >> 16) & 0xF000) == 0xA000 ||
            entry is SharpCompress.Common.Rar.RarEntry { IsRedir: true } ||
            !string.IsNullOrEmpty(entry.LinkTarget))
            return "符号链接或硬链接不可提取";
        if (!entry.IsComplete || entry.IsSplitAfter) return "缺少分卷";
        if (entry.Size < 0) return "文件大小无效";
        return SafeExtractionPath.Validate(entry.Key ?? "", entry.IsDirectory);
    }

    /// <summary>清理可复用的分段缓存，归档仍可继续按需读取。</summary>
    internal void ClearCache() { _stream.ClearCache(); }
    /// <summary>设置网络分段读取进度回调；传入空值可移除回调。</summary>
    internal void SetReadProgress(Action<long, long>? callback) { _stream.ReadProgress = callback; }
    /// <summary>根据远端压缩包大小选择平衡网络往返次数和单次预取量的分段大小。</summary>
    private static int SelectBlockSize(long archiveLength, bool solid)
    {
        var size = archiveLength switch
        {
            <= 64L * 1024 * 1024 => HttpRangeStream.BlockSize,
            <= 256L * 1024 * 1024 => 256 * 1024,
            <= 1024L * 1024 * 1024 => 512 * 1024,
            <= 4L * 1024 * 1024 * 1024 => 1024 * 1024,
            _ => 2 * 1024 * 1024
        };
        if (solid) size = Math.Min(size * 2, 4 * 1024 * 1024);
        return size;
    }
    /// <summary>获取当前分段缓存总占用。</summary>
    internal long GetCacheSize() { return _stream.GetCacheSize(); }
    /// <summary>释放解码器和内存分段，磁盘缓存由容量及期限策略管理。</summary>
    public void Dispose() { _encryptedZip?.Close(); _archive.Dispose(); _stream.Dispose(); }
}

/// <summary>只包含展示所需数据的归档条目，Index 与解码器条目一一对应。</summary>
internal sealed record ArchiveItem(int Index, string Path, bool IsDirectory, long Size, long CompressedSize,
    DateTime? Modified, bool Encrypted, string BlockedReason);
/// <summary>传递当前输出文件及本批次已写入字节数的进度快照。</summary>
internal sealed record ExtractionProgress(string Name, long Written, long Total, int CompletedFiles, int TotalFiles);

/// <summary>独立于操作系统的归档路径检查，不允许目录穿越、绝对路径、设备名和链接目录。</summary>
internal static class SafeExtractionPath
{
    /// <summary>检查归档相对路径；返回空字符串表示可用。</summary>
    internal static string Validate(string path, bool directory = false)
    {
        var normalized = path.Replace('\\', '/');
        if (directory) normalized = normalized.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Length > 2000) return "路径无效或过长";
        foreach (var part in normalized.Split('/'))
        {
            if (part is "" or "." or ".." || part.Length > 240 || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(character => character < 32 || "<>:\"|?*".Contains(character))) return "包含不安全的路径";
            var name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" ||
                name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && char.IsDigit(name[3])) return "包含系统保留名称";
        }
        return "";
    }

    /// <summary>解析下载目录下的相对文件路径，并验证最终路径没有离开根目录。</summary>
    internal static string Resolve(string root, string relative)
    {
        var error = Validate(relative);
        if (error.Length != 0) throw new IOException(error);
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(absoluteRoot, relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(absoluteRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("压缩包条目试图离开下载目录。");
        return path;
    }

    /// <summary>逐层创建下载目录并拒绝已存在的符号链接或目录联接。</summary>
    internal static void EnsureDirectories(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && parent != path) EnsureDirectories(parent);
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("下载路径包含符号链接或目录联接，请选择普通目录。");
        }
        else Directory.CreateDirectory(path);
    }
}

/// <summary>ZIP 条目完整性检查，采用标准 CRC32 多项式。</summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();
    /// <summary>创建 256 项查找表。</summary>
    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
    /// <summary>继续计算一个数据块的 CRC，初始值为 uint.MaxValue，最终值取反。</summary>
    internal static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) crc = Table[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }
}
