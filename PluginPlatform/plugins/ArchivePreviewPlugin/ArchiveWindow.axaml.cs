using System.Diagnostics;
using System.Globalization;
using ArchivePreviewPlugin.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Drive.Plugin.SDK;

namespace ArchivePreviewPlugin;

/// <summary>压缩包浏览窗口；窗口状态在 UI 线程更新，网络和解码任务在后台串行执行。</summary>
public sealed partial class ArchiveWindow : Window
{
    private readonly PluginContext? _drive;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime;
    private CancellationTokenSource? _operation;
    private Task _job = Task.CompletedTask;
    private int _jobVersion;
    private bool _closed;
    private bool _busy;
    private ArchiveSession? _session;
    private FileEntry? _file;
    private string _directory = "";
    private string? _cacheRoot;

    /// <summary>设计器和界面检查使用的构造函数，不访问网盘或执行网络请求。</summary>
    public ArchiveWindow() : this(null, new HttpClient(), CancellationToken.None) { }

    /// <summary>创建与主程序同配色的窗口，绑定当前启用周期的取消令牌。</summary>
    /// <param name="drive">插件 API 上下文；设计器可为空。</param>
    /// <param name="http">插件共享的 HTTP 连接池。</param>
    /// <param name="enabled">插件停用时被取消的令牌。</param>
    public ArchiveWindow(PluginContext? drive, HttpClient http, CancellationToken enabled)
    {
        _drive = drive; _http = http;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(enabled);
        InitializeComponent();
        Icon = new WindowIcon(new MemoryStream(Convert.FromBase64String(IconData.Ico)));
        Closed += OnClosed;
    }

    /// <summary>取消旧工作后获取文件的新元数据和临时链接，只建立目录索引。</summary>
    /// <param name="file">主程序菜单或选择器提供的网盘文件。</param>
    /// <returns>目录加载完成、取消或错误已呈现在界面后的任务。</returns>
    public Task OpenArchiveAsync(FileEntry file)
    {
        return RunAsync(async token =>
        {
            if (_drive is null) return;
            _session?.Dispose(); _session = null; _file = file;
            if (!Plugin.Extensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("请选择 ZIP、7z、RAR 或未压缩的 TAR 文件。");
            EntryList.ItemsSource = Array.Empty<ArchiveRow>();
            EmptyPanel.IsVisible = true; EmptyTitle.Text = "正在读取压缩包目录";
            EmptyDescription.Text = "按需读取索引，已有缓存会自动复用。";
            ArchiveTitle.Text = file.Name;
            ArchiveSubtitle.Text = "正在获取临时链接与文件信息…";
            FormatNotice.IsVisible = false;
            var system = await _drive.System.GetInfoAsync(token);
            var user = await _drive.User.GetCurrentAsync(token);
            if (!user.IsLoggedIn) throw new IOException("请先登录网盘。");
            var freshFile = await _drive.Files.GetAsync(file.Id, token);
            var password = PasswordBox.Text;
            // 使用账户根目录、文件 ID、内容摘要和修改时间隔离缓存，不保存下载 URL。
            var identity = user.Id + "|" + user.RootFolderId + "|" + freshFile.Id + "|" + freshFile.Hash + "|" + freshFile.ModifiedAt.ToString("O");
            var cacheRoot = Path.Combine(system.PluginTempDirectory, "archive-preview-blocks-v1");
            _cacheRoot = cacheRoot;
            var loaded = await Task.Run(async () =>
            {
                var stream = await HttpRangeStream.OpenAsync(_http,
                    cancellation => _drive.Files.GetDownloadUrlAsync(freshFile.Id, cancellation), cacheRoot, identity,
                    !string.IsNullOrEmpty(freshFile.Hash), checked((long)freshFile.Size), token).ConfigureAwait(false);
                return ArchiveSession.Open(stream, Path.GetExtension(freshFile.Name), password, token);
            }, token);
            if (_closed || token.IsCancellationRequested) { loaded.Dispose(); token.ThrowIfCancellationRequested(); return; }
            _session = loaded; _file = freshFile; _directory = "";
            FilterBox.Text = ""; PickerPanel.IsVisible = false;
            ArchiveTitle.Text = freshFile.Name;
            ArchiveSubtitle.Text = $"{loaded.Format} · {FormatSize(loaded.ArchiveSize)} · {loaded.Items.Count(item => !item.IsDirectory):N0} 个文件";
            FormatNotice.Text = loaded.IsSolid
                ? "固实压缩：目录可按需浏览，提取文件时可能需要读取并解码同一压缩块中的前序数据。"
                : "双击目录进入；可多选文件或目录。小压缩包可能落在一个缓存块内。";
            FormatNotice.IsVisible = true;
            RefreshRows(); UpdateCacheText();
            StatusText.Text = "目录已就绪，选择需要下载的文件";
            _drive.Logger.Info($"已预览 {freshFile.Name}：{loaded.Items.Length} 个条目，网络读取 {FormatSize(loaded.NetworkBytes)}。");
        }, "正在读取压缩包目录…");
    }

    /// <summary>重新读取目录；可用于输入密码后重试，缓存仍可复用。</summary>
    private async void ReloadClick(object? sender, RoutedEventArgs args)
    {
        if (_file is not null) await OpenArchiveAsync(_file);
    }

    /// <summary>列出主程序当前网盘目录中的压缩包，按接口页大小读取完整文件列表。</summary>
    private async void ChooseArchiveClick(object? sender, RoutedEventArgs args)
    {
        await RunAsync(async token =>
        {
            if (_drive is null) return;
            var folder = await _drive.Files.GetCurrentFolderAsync(token);
            var choices = new List<FileEntry>();
            for (var page = 1; ; page++)
            {
                var result = await _drive.Files.ListAsync(folder, page, 200, token);
                choices.AddRange(result.Files.Where(file => Plugin.Extensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase)));
                if (result.Files.Length == 0 || page * 200 >= result.TotalFiles) break;
                if (page >= 100) throw new IOException("当前目录文件过多，请从主程序的具体文件菜单打开预览。");
            }
            ArchiveChoices.ItemsSource = choices.OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ArchiveChoices.SelectedIndex = choices.Count > 0 ? 0 : -1;
            PickerPanel.IsVisible = true;
            StatusText.Text = choices.Count == 0 ? "当前网盘目录没有支持的压缩包，请在主程序切换目录后重试" : $"当前目录找到 {choices.Count} 个压缩包";
        }, "正在读取网盘当前目录…");
    }

    /// <summary>打开选择器中的压缩包。</summary>
    private async void OpenChoiceClick(object? sender, RoutedEventArgs args)
    {
        if (ArchiveChoices.SelectedItem is FileEntry file) await OpenArchiveAsync(file);
    }

    /// <summary>根据当前目录或搜索词从内存索引生成列表，不发起网络请求。</summary>
    private void RefreshRows()
    {
        if (_session is null) return;
        var filter = FilterBox.Text?.Trim() ?? "";
        var rows = new List<ArchiveRow>();
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _session.Items)
        {
            var path = item.Path.Replace('\\', '/').TrimEnd('/');
            if (filter.Length > 0)
            {
                if (!item.IsDirectory && path.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) rows.Add(ArchiveRow.FromFile(item, path));
                continue;
            }
            if (!path.StartsWith(_directory, StringComparison.Ordinal)) continue;
            var remainder = path[_directory.Length..];
            if (remainder.Length == 0) continue;
            var slash = remainder.IndexOf('/');
            if (slash >= 0 || item.IsDirectory)
            {
                var name = slash >= 0 ? remainder[..slash] : remainder;
                if (name.Length == 0) continue;
                var key = _directory + name + "/";
                if (folders.Add(key)) rows.Add(ArchiveRow.FromFolder(name, key));
            }
            else rows.Add(ArchiveRow.FromFile(item, remainder));
        }
        EntryList.ItemsSource = rows.OrderByDescending(row => row.IsFolder).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Breadcrumb.Text = filter.Length > 0 ? "搜索结果 · 整个压缩包" : _directory.Length == 0 ? "根目录" : "/" + _directory;
        UpButton.IsEnabled = !_busy && (_directory.Length > 0 || filter.Length > 0);
        EmptyPanel.IsVisible = rows.Count == 0;
        EmptyTitle.Text = filter.Length > 0 ? "没有匹配的文件" : "这个目录是空的";
        EmptyDescription.Text = filter.Length > 0 ? "尝试其他文件名或路径关键词。" : "当前目录没有可浏览的文件。";
        UpdateSelection();
    }

    /// <summary>搜索框变化时更新本地列表。</summary>
    private void FilterChanged(object? sender, TextChangedEventArgs args)
    {
        if (EntryList is not null) RefreshRows();
    }

    /// <summary>返回上级目录；搜索状态先清除搜索词。</summary>
    private void UpClick(object? sender, RoutedEventArgs args)
    {
        if (!string.IsNullOrEmpty(FilterBox.Text)) FilterBox.Text = "";
        else
        {
            var trimmed = _directory.TrimEnd('/');
            var slash = trimmed.LastIndexOf('/');
            _directory = slash < 0 ? "" : trimmed[..(slash + 1)];
            RefreshRows();
        }
    }

    /// <summary>双击文件夹进入；文件不会被自动下载或执行。</summary>
    private void EntryDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (_busy) return;
        // Toggle 多选模式下第二次点击可能取消选中，必须根据被双击的行定位。
        var source = args.Source as Control;
        var row = source?.DataContext as ArchiveRow
            ?? source?.GetVisualAncestors().OfType<Control>().Select(control => control.DataContext).OfType<ArchiveRow>().FirstOrDefault();
        if (row is { IsFolder: true })
        {
            _directory = row.FolderPath; FilterBox.Text = ""; RefreshRows();
        }
    }

    /// <summary>Enter 进入目录，Backspace 返回上层。</summary>
    private void EntryKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { EnterSelectedFolder(); args.Handled = true; }
        else if (args.Key == Key.Back && UpButton.IsEnabled) { UpClick(sender, args); args.Handled = true; }
    }

    /// <summary>定位到最近选择的文件夹。</summary>
    private void EnterSelectedFolder()
    {
        if (_busy) return;
        if (EntryList.SelectedItem is ArchiveRow { IsFolder: true } folder)
        {
            _directory = folder.FolderPath; FilterBox.Text = ""; RefreshRows();
        }
    }

    /// <summary>选择当前列表的全部条目。</summary>
    private void SelectAllClick(object? sender, RoutedEventArgs args) { EntryList.SelectAll(); }
    /// <summary>选择变化时刷新可提取文件数与下载按钮。</summary>
    private void EntriesSelectionChanged(object? sender, SelectionChangedEventArgs args) { UpdateSelection(); }

    /// <summary>将选定目录展开到安全文件索引，自动去除重叠选择。</summary>
    private int[] GetSelectedIndices()
    {
        if (_session is null || EntryList.SelectedItems is null) return [];
        var indices = new HashSet<int>();
        foreach (var row in EntryList.SelectedItems.OfType<ArchiveRow>())
        {
            if (!row.IsFolder)
            {
                if (_session.Items[row.EntryIndex].BlockedReason.Length == 0) indices.Add(row.EntryIndex);
                continue;
            }
            foreach (var item in _session.Items)
            {
                if (!item.IsDirectory && item.BlockedReason.Length == 0 && item.Path.Replace('\\', '/').StartsWith(row.FolderPath, StringComparison.Ordinal)) indices.Add(item.Index);
            }
        }
        return indices.ToArray();
    }

    /// <summary>展示实际会下载的文件数量；不安全的条目不参与提取。</summary>
    private void UpdateSelection()
    {
        if (SelectionText is null) return;
        var indices = GetSelectedIndices();
        var count = (EntryList.ItemsSource as ArchiveRow[])?.Length ?? 0;
        SelectionText.Text = _session is null ? "尚未打开压缩包" : $"当前 {count:N0} 项 · 已选 {indices.Length:N0} 个可提取文件";
        DownloadButton.IsEnabled = !_busy && indices.Length > 0;
    }

    /// <summary>读取最新下载设置，把选中的归档文件流式保存到该目录，保留包内相对路径。</summary>
    private async void DownloadClick(object? sender, RoutedEventArgs args)
    {
        var indices = GetSelectedIndices();
        var session = _session;
        if (session is null || indices.Length == 0) return;
        await RunAsync(async token =>
        {
            if (_drive is null) return;
            var destination = await _drive.Downloads.GetSaveDirectoryAsync(token);
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
                throw new IOException("请先在主程序设置中指定有效的绝对下载目录。");
            DestinationText.Text = destination;
            WorkProgress.IsIndeterminate = false; WorkProgress.Value = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = -100L;
            var version = _jobVersion;
            session.SetReadProgress((read, archiveLength) =>
            {
                // 固实归档跳过前序条目时没有输出字节，使用网络读取量持续反馈当前阶段。
                if (stopwatch.ElapsedMilliseconds - lastReport < 200) return;
                lastReport = stopwatch.ElapsedMilliseconds;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_closed || version != _jobVersion || !_busy) return;
                    WorkProgress.IsIndeterminate = true;
                    StatusText.Text = session.IsSolid
                        ? $"正在读取压缩块 · 已读取 {FormatSize(read)} / {FormatSize(archiveLength)}"
                        : $"正在读取压缩数据 · 已读取 {FormatSize(read)} / {FormatSize(archiveLength)}";
                    UpdateCacheText(false);
                });
            });
            string[] output;
            try
            {
                output = await Task.Run(() => session.Extract(indices, destination, progress =>
                {
                    // 限制 UI 更新频率，解压多个小块时不会淹没 Dispatcher。
                    if (stopwatch.ElapsedMilliseconds - lastReport < 100 && progress.Written < progress.Total) return;
                    lastReport = stopwatch.ElapsedMilliseconds;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_closed || version != _jobVersion || !_busy) return;
                        WorkProgress.IsIndeterminate = false;
                        WorkProgress.Value = progress.Total > 0 ? progress.Written * 100d / progress.Total : 100;
                        StatusText.Text = $"正在提取 {progress.Name} · {FormatSize(progress.Written)} / {FormatSize(progress.Total)}";
                        UpdateCacheText(false);
                    });
                }, token), token);
            }
            finally
            {
                session.SetReadProgress(null);
            }
            StatusText.Text = $"已保存 {output.Length} 个文件到下载目录";
            _drive.Logger.Info($"从 {_file?.Name} 提取了 {output.Length} 个文件到下载目录。");
            UpdateCacheText();
        }, session.IsSolid ? "正在读取所选文件所在压缩块；固实压缩可能需要读取前序数据…" : "正在提取选中的文件…");
    }

    /// <summary>关闭正在进行的请求，不删除已完成文件；未完成的 .part 文件由提取器清理。</summary>
    private void CancelClick(object? sender, RoutedEventArgs args) { _operation?.Cancel(); }

    /// <summary>在没有后台读取时清空分段缓存，不影响已经下载的文件。</summary>
    private async void ClearCacheClick(object? sender, RoutedEventArgs args)
    {
        if (_session is null && _cacheRoot is null) return;
        await RunAsync(async token =>
        {
            await Task.Run(() =>
            {
                if (_session is not null) _session.ClearCache();
                else if (_cacheRoot is not null) new BlockCache(_cacheRoot, "manual-clear").Clear();
            }, token);
            UpdateCacheText(); StatusText.Text = "分段缓存已清理";
        }, "正在清理缓存…");
    }

    /// <summary>取消旧任务并等待其释放归档流，然后开始最新一次操作。</summary>
    private async Task RunAsync(Func<CancellationToken, Task> work, string status)
    {
        var version = ++_jobVersion;
        _operation?.Cancel();
        await _job;
        if (_closed || _lifetime.IsCancellationRequested || version != _jobVersion) return;
        _job = RunCoreAsync(work, status);
        await _job;
    }

    /// <summary>统一管理取消、异常展示和按钮状态，异步事件不会向运行器抛出未处理异常。</summary>
    private async Task RunCoreAsync(Func<CancellationToken, Task> work, string status)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        SetBusy(true); StatusText.ClearValue(TextBlock.ForegroundProperty); StatusText.Text = status;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "已取消；已完成文件保留，未完成文件已清理"; }
        catch (Exception error)
        {
            if (!_closed)
            {
                var message = error is HttpRequestException ? "网络连接失败，请检查连接后重试。" : error.Message;
                StatusText.Text = message;
                StatusText.Foreground = this.FindResource("ErrorColor") as IBrush;
                if (_session is null)
                {
                    EmptyPanel.IsVisible = true; EmptyTitle.Text = "暂时无法预览";
                    EmptyDescription.Text = "检查压缩包是否完整、是否需要密码，然后点击重新读取。";
                }
                _drive?.Logger.Error("压缩包操作失败：" + message);
            }
        }
        finally
        {
            _operation = null;
            if (!_closed) { SetBusy(false); UpdateCacheText(); }
        }
    }

    /// <summary>在耗时操作期间禁用冲突按钮，保留取消与窗口关闭。</summary>
    private void SetBusy(bool value)
    {
        _busy = value;
        ChooseButton.IsEnabled = !value; ReloadButton.IsEnabled = !value && _file is not null;
        PickerPanel.IsEnabled = !value; PasswordBox.IsEnabled = !value;
        ClearCacheButton.IsEnabled = !value && (_session is not null || _cacheRoot is not null);
        SelectAllButton.IsEnabled = !value && _session is not null; EntryList.IsEnabled = !value;
        UpButton.IsEnabled = !value && (_directory.Length > 0 || !string.IsNullOrEmpty(FilterBox.Text));
        CancelButton.IsEnabled = value; WorkProgress.IsVisible = value; WorkProgress.IsIndeterminate = value;
        UpdateSelection();
    }

    /// <summary>更新本次读取量和缓存信息；频繁进度更新时不扫描磁盘。</summary>
    private void UpdateCacheText(bool includeDiskSize = true)
    {
        if (_session is null) return;
        try
        {
            var suffix = includeDiskSize ? " · 缓存 " + FormatSize(_session.GetCacheSize()) : "";
            CacheText.Text = $"已读取 {FormatSize(_session.NetworkBytes)} · 复用 {FormatSize(_session.CacheHitBytes)}{suffix}";
        }
        catch (IOException) { CacheText.Text = "缓存目录暂时不可读取"; }
        catch (UnauthorizedAccessException) { CacheText.Text = "缓存目录暂时不可读取"; }
    }

    /// <summary>格式化文件大小，单位与主程序一样采用 1024 进位。</summary>
    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = Math.Max(0, bytes); var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return size.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>窗口关闭后取消请求，等待后台退出后再释放归档对象，防止并发 Dispose。</summary>
    private async void OnClosed(object? sender, EventArgs args)
    {
        _closed = true; _lifetime.Cancel();
        await _job;
        _session?.Dispose(); _session = null;
        _lifetime.Dispose();
        if (_drive is null) _http.Dispose();
    }

    /// <summary>为离线界面检查填充展示数据，不调用宿主 API。</summary>
    internal void SetPreviewRows(ArchiveRow[] rows)
    {
        ArchiveTitle.Text = "项目资料.zip";
        ArchiveSubtitle.Text = "ZIP · 128 MiB · 24 个文件";
        EntryList.ItemsSource = rows; EmptyPanel.IsVisible = false;
        SelectionText.Text = "当前 5 项 · 已选 0 个文件";
        CacheText.Text = "已读取 128 KiB · 缓存 128 KiB";
        StatusText.Text = "目录已就绪，选择需要下载的文件";
    }
}

/// <summary>归档列表行；文件与合成目录采用同一套 Avalonia 编译绑定。</summary>
public sealed class ArchiveRow
{
    private const string FileIcon = "\uf15b";
    private const string FolderIcon = "\uf07b";
    /// <summary>界面显示的名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>完整路径、加密状态及不可提取原因。</summary>
    public string Detail { get; init; } = "";
    /// <summary>行内辅助说明。</summary>
    public string Hint { get; init; } = "";
    /// <summary>是否显示辅助说明。</summary>
    public bool HasHint { get { return Hint.Length > 0; } }
    /// <summary>是否为可进入的目录。</summary>
    public bool IsFolder { get; init; }
    /// <summary>归档内部完整目录前缀，使用正斜杠。</summary>
    public string FolderPath { get; init; } = "";
    /// <summary>原始文件条目编号；目录不使用此字段。</summary>
    public int EntryIndex { get; init; } = -1;
    /// <summary>文件原始大小的显示文本。</summary>
    public string SizeText { get; init; } = "—";
    /// <summary>修改时间的显示文本。</summary>
    public string ModifiedText { get; init; } = "—";
    /// <summary>Font Awesome 图标字符，使用插件内嵌的字体资源显示。</summary>
    public string Icon { get; init; } = FileIcon;

    /// <summary>将归档元数据转换为文件展示行。</summary>
    internal static ArchiveRow FromFile(ArchiveItem item, string name)
    {
        var hint = item.BlockedReason.Length > 0 ? "不可提取 · " + item.BlockedReason : item.Encrypted ? "加密文件 · 请先输入密码并重新读取" : "";
        return new ArchiveRow
        {
            Name = name, Detail = item.Path + (hint.Length > 0 ? "\n" + hint : ""), Hint = hint,
            EntryIndex = item.Index, SizeText = ArchiveWindow.FormatSize(item.Size), Icon = GetFileIcon(item.Path),
            ModifiedText = item.Modified?.ToString("yyyy-MM-dd HH:mm") ?? "—"
        };
    }

    /// <summary>根据文件后缀选择 Font Awesome 图标；未知格式使用通用文件图标。</summary>
    /// <param name="path">归档内的完整文件路径，支持正斜杠和反斜杠。</param>
    /// <returns>插件内嵌 Font Awesome Solid 字体中的图标字符。</returns>
    private static string GetFileIcon(string path)
    {
        return Path.GetExtension(path.Replace('\\', '/')).ToLowerInvariant() switch
        {
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" => "\uf1c6",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".tif" or ".tiff" or ".avif" => "\uf03e",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" or ".flv" or ".m4v" => "\uf008",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" or ".opus" => "\uf001",
            ".pdf" => "\uf1c1",
            ".doc" or ".docx" or ".odt" or ".rtf" => "\uf1c2",
            ".xls" or ".xlsx" or ".ods" or ".csv" => "\uf1c3",
            ".ppt" or ".pptx" or ".odp" => "\uf1c4",
            ".cs" or ".axaml" or ".xaml" or ".csproj" or ".xml" or ".json" or ".js" or ".ts" or ".jsx" or ".tsx" or ".vue" or ".html" or ".css" or ".py" or ".java" or ".c" or ".cpp" or ".h" or ".go" or ".rs" or ".sql" or ".sh" or ".ps1" => "\uf1c9",
            ".txt" or ".md" or ".log" or ".ini" or ".yaml" or ".yml" or ".toml" => "\uf15c",
            _ => FileIcon
        };
    }

    /// <summary>创建由条目路径合成的目录行。</summary>
    internal static ArchiveRow FromFolder(string name, string path)
    {
        return new ArchiveRow { Name = name, Detail = path, IsFolder = true, FolderPath = path,
            Icon = FolderIcon };
    }
}
