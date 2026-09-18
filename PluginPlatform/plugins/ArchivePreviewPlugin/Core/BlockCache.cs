using System.Security.Cryptography;
using System.Text;

namespace ArchivePreviewPlugin.Core;

/// <summary>有容量和期限限制的分段磁盘缓存；文件名只来自摘要，不保存临时链接或密码。</summary>
internal sealed class BlockCache
{
    internal const long Capacity = 256L * 1024 * 1024;
    private readonly string _root;
    private readonly string _prefix;
    private readonly object _gate = new();
    private long _estimatedBytes = -1;
    private DateTime _nextPrune;

    /// <summary>按用户、网盘文件版本和远端验证器隔离缓存，并清理过期分段。</summary>
    internal BlockCache(string root, string identity)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _prefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        Prune(0);
    }

    /// <summary>读取完整且长度符合预期的分段；缺失或损坏的分段重新请求。</summary>
    internal byte[]? Read(long offset, int length)
    {
        lock (_gate)
        {
            var path = GetPath(offset);
            if (!File.Exists(path)) return null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length != length) { File.Delete(path); return null; }
                var data = File.ReadAllBytes(path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return data;
            }
            catch (IOException) { return null; }
        }
    }

    /// <summary>分段完整下载后原子提交，容量超过 256 MiB 时优先淘汰最久未访问的缓存。</summary>
    internal void Write(long offset, byte[] data)
    {
        lock (_gate)
        {
            Prune(data.Length);
            var path = GetPath(offset);
            var previousSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                File.Move(temporary, path, true);
                _estimatedBytes += data.Length - previousSize;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    /// <summary>计算当前插件所有归档分段占用的磁盘字节数。</summary>
    internal long GetSize()
    {
        return Directory.EnumerateFiles(_root, "*.block").Sum(path => new FileInfo(path).Length);
    }

    /// <summary>只清理本缓存目录中的分段和未完成的临时文件，不递归操作外部目录。</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            foreach (var path in Directory.EnumerateFiles(_root, "*.block")) File.Delete(path);
            _estimatedBytes = 0;
        }
    }

    /// <summary>生成摘要和数值偏移组成的本地缓存文件路径。</summary>
    private string GetPath(long offset)
    {
        return Path.Combine(_root, _prefix + "-" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".block");
    }

    /// <summary>清理七天未使用的数据，并为新分段腾出空间。</summary>
    private void Prune(int incoming)
    {
        if (_estimatedBytes >= 0 && _estimatedBytes + incoming <= Capacity && DateTime.UtcNow < _nextPrune) return;
        var files = Directory.EnumerateFiles(_root, "*.block").Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToArray();
        long total = files.Sum(file => file.Length);
        // 一次预留 16 MiB 空间，避免到达容量上限后每读取 64 KiB 都扫描数千个文件。
        var reserve = incoming > 0 ? Math.Max(incoming, 16 * 1024 * 1024) : 0;
        foreach (var file in files)
        {
            if (total + reserve <= Capacity && file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) break;
            try { var length = file.Length; file.Delete(); total -= length; }
            catch (IOException) { }
        }
        _estimatedBytes = total;
        _nextPrune = DateTime.UtcNow.AddMinutes(5);
        foreach (var path in Directory.EnumerateFiles(_root, "*.tmp"))
        {
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.Delete(path);
        }
    }
}
