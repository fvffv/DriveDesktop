using System.Collections.ObjectModel;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Drive.Plugin.SDK;
using ImageToolsPlugin.Core;

namespace ImageToolsPlugin;

/// <summary>图片处理窗口；界面操作串行调度，图片解码和 OCR 在后台执行。</summary>
public sealed partial class ImageToolsWindow : Window
{
    private readonly PluginContext? _drive;
    private readonly CancellationTokenSource _lifetime;
    private readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
        { Timeout = TimeSpan.FromSeconds(90) };
    private readonly ObservableCollection<ImageAsset> _assets = new();
    private readonly Dictionary<string, RecognizedLine[]> _recognized = new(StringComparer.Ordinal);
    private readonly OcrService _ocr = new();
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "DriveImageTools", Guid.NewGuid().ToString("N"));
    private readonly Stack<(string Id, string Title)> _cloudHistory = new();
    private CancellationTokenSource? _operation;
    private Task _job = Task.CompletedTask;
    private ImageAsset? _current;
    private bool _closed, _busy, _updatingCrop, _updatingList;
    private int _jobVersion, _cloudPage = 1;
    private string _cloudFolder = "", _cloudTitle = "根目录";

    /// <summary>设计器构造函数，不访问宿主。</summary>
    public ImageToolsWindow() : this(null, CancellationToken.None) { }

    /// <summary>创建插件窗口，使用宿主生命周期作为取消源。</summary>
    public ImageToolsWindow(PluginContext? drive, CancellationToken enabled)
    {
        _drive = drive; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(enabled);
        Directory.CreateDirectory(_temporary);
        InitializeComponent();
        if (IconData.Ico.Length > 0) Icon = new WindowIcon(new MemoryStream(Convert.FromBase64String(IconData.Ico)));
        ImageList.ItemsSource = _assets;
        Preview.SelectionChanged += PreviewSelectionChanged;
        Closed += WindowClosed;
        SetBusy(false);
    }

    /// <summary>以任务队列方式添加菜单传来的云端图片，保留窗口中已有图片。</summary>
    public Task AddCloudFilesAsync(FileEntry[] files)
    {
        return RunAsync(async token =>
        {
            if (_drive is null) throw new IOException("当前窗口没有连接网盘。");
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                if (file.IsFolder || !IsSupported(file.Name)) continue;
                CheckCount();
                var target = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".input");
                try
                {
                    StatusText.Text = "正在读取云端图片：" + file.Name;
                    var fresh = await _drive.Files.GetAsync(file.Id, token);
                    if (fresh.Size > (ulong)ImageProcessor.MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var link = await _drive.Files.GetDownloadUrlAsync(file.Id, token);
                        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                            throw new IOException("网盘返回的图片链接无效。");
                        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(token);
                        transfer.CancelAfter(TimeSpan.FromSeconds(90));
                        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, transfer.Token);
                        var expired = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Gone ||
                            response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json";
                        if (expired && attempt == 0) continue;
                        response.EnsureSuccessStatusCode();
                        if (expired) throw new IOException("图片临时链接已过期，请重新添加。");
                        if (response.Content.Headers.ContentLength > ImageProcessor.MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
                        await using var source = await response.Content.ReadAsStreamAsync(transfer.Token);
                        try { await ImageProcessor.CopyInputAsync(source, target, transfer.Token); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            throw new IOException("云端图片读取超时，请重新添加。");
                        }
                        break;
                    }
                    await ImportAsync(target, file.Name, "云端", token);
                }
                catch { if (File.Exists(target)) File.Delete(target); throw; }
            }
            StatusText.Text = $"已添加图片，当前共 {_assets.Count} 张";
        }, "正在添加云端图片…");
    }

    /// <summary>打开系统文件选择器，多选本地图片后复制到插件临时目录。</summary>
    private async void LocalClick(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async token =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "添加本地图片", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("图片") { Patterns = Plugin.Extensions.Select(x => "*" + x).ToArray() }]
            });
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested(); CheckCount();
                var target = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".input");
                try
                {
                    await using (var source = await file.OpenReadAsync()) await ImageProcessor.CopyInputAsync(source, target, token);
                    await ImportAsync(target, file.Name, "本地", token);
                }
                catch { if (File.Exists(target)) File.Delete(target); throw; }
            }
            StatusText.Text = $"当前共 {_assets.Count} 张图片";
        }, "选择本地图片…");
    }

    /// <summary>验证输入图片，记录尺寸和来源，随后选中并生成预览。</summary>
    private async Task ImportAsync(string path, string name, string source, CancellationToken token)
    {
        var asset = await Task.Run(() =>
        {
            using var bitmap = ImageProcessor.Decode(path);
            token.ThrowIfCancellationRequested();
            return new ImageAsset(name, path, bitmap.Width, bitmap.Height, new FileInfo(path).Length, source);
        }, token);
        token.ThrowIfCancellationRequested();
        _updatingList = true;
        try { _assets.Add(asset); ImageList.SelectedItem = asset; }
        finally { _updatingList = false; }
        await ShowPreviewAsync(asset, token);
    }

    /// <summary>限制单次会话的图片数量，避免意外批量占用磁盘。</summary>
    private void CheckCount()
    {
        if (_assets.Count >= 50) throw new IOException("一次最多处理 50 张图片，请导出后移除一部分再添加。");
    }

    /// <summary>判断受支持的文件后缀；后缀匹配不区分大小写。</summary>
    private static bool IsSupported(string name)
    {
        return Plugin.Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>切换左侧图片时更新当前预览，不执行任何导出。</summary>
    private async void ImageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingList || ImageList.SelectedItem is not ImageAsset asset) return;
        await RunAsync(token => ShowPreviewAsync(asset, token), "正在生成预览…");
    }

    /// <summary>生成最长边 1400 像素的预览，原始尺寸另行保留供精确裁剪。</summary>
    private async Task ShowPreviewAsync(ImageAsset asset, CancellationToken token)
    {
        var result = await Task.Run(() =>
        {
            using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
            using var small = ImageProcessor.Resize(bitmap, 1400);
            return (Data: ImageProcessor.Encode(small, "png", 100), bitmap.Width, bitmap.Height);
        }, token);
        token.ThrowIfCancellationRequested();
        asset.Width = result.Width; asset.Height = result.Height; _current = asset;
        Preview.SetImage(new Bitmap(new MemoryStream(result.Data)), asset.Width, asset.Height);
        PreviewTitle.Text = $"{asset.Name} · {asset.Width} × {asset.Height}";
        EmptyHint.IsVisible = false;
        SetCropFields(new PixelRect(0, 0, asset.Width, asset.Height));
        OcrText.Text = _recognized.TryGetValue(asset.WorkingPath, out var lines) ? TextOf(lines) : "";
        CountText.Text = $"图片 · {_assets.Count}";
        StatusText.Text = "图片已就绪，可裁剪、提取文字或导出";
    }

    /// <summary>将鼠标裁剪框同步到坐标输入框。</summary>
    private void PreviewSelectionChanged(object? sender, EventArgs e)
    {
        CropExpander.IsExpanded = true;
        SetCropFields(Preview.Selection);
    }

    /// <summary>更新裁剪输入值时抑制相互触发的变化事件。</summary>
    private void SetCropFields(PixelRect rect)
    {
        _updatingCrop = true;
        try { CropX.Value = rect.X; CropY.Value = rect.Y; CropWidth.Value = rect.Width; CropHeight.Value = rect.Height; }
        finally { _updatingCrop = false; }
    }

    /// <summary>坐标合法时将输入区域显示在图片上，越界输入由应用裁剪时提示。</summary>
    private void CropValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_updatingCrop || _current is null || CropHeight is null) return;
        var rect = CropRect();
        if (rect.X >= 0 && rect.Y >= 0 && rect.Width > 0 && rect.Height > 0 &&
            rect.Right <= _current.Width && rect.Bottom <= _current.Height) Preview.SetSelection(rect);
    }

    /// <summary>读取裁剪输入框里的整数像素坐标。</summary>
    private PixelRect CropRect()
    {
        return new PixelRect((int)(CropX.Value ?? 0), (int)(CropY.Value ?? 0), (int)(CropWidth.Value ?? 1), (int)(CropHeight.Value ?? 1));
    }

    /// <summary>生成独立的裁剪版本并更新预览；保留原始图片以便恢复。</summary>
    private async void CropClick(object? sender, RoutedEventArgs e)
    {
        var asset = _current; if (asset is null) return;
        var rect = CropRect();
        await RunAsync(async token =>
        {
            var target = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                await Task.Run(() =>
                {
                    using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
                    using var cropped = ImageProcessor.Crop(bitmap, rect.X, rect.Y, rect.Width, rect.Height);
                    File.WriteAllBytes(target, ImageProcessor.Encode(cropped, "png", 100));
                }, token);
                token.ThrowIfCancellationRequested();
                var old = asset.WorkingPath; asset.WorkingPath = target; _recognized.Remove(old);
                if (old != asset.OriginalPath) File.Delete(old);
                await ShowPreviewAsync(asset, token); RefreshList();
                CropExpander.IsExpanded = false;
                StatusText.Text = "裁剪已应用，可导出当前图片或恢复原图";
            }
            catch { if (asset.WorkingPath != target && File.Exists(target)) File.Delete(target); throw; }
        }, "正在裁剪…");
    }

    /// <summary>丢弃当前裁剪版本，重新显示原始图片。</summary>
    private async void ResetClick(object? sender, RoutedEventArgs e)
    {
        var asset = _current; if (asset is null) return;
        await RunAsync(async token =>
        {
            var old = asset.WorkingPath; asset.WorkingPath = asset.OriginalPath;
            if (old != asset.OriginalPath) { _recognized.Remove(old); File.Delete(old); }
            await ShowPreviewAsync(asset, token); RefreshList();
            StatusText.Text = "已恢复原图";
        }, "正在恢复原图…");
    }

    /// <summary>移除当前图片及其插件临时文件，不删除用户源文件。</summary>
    private async void RemoveClick(object? sender, RoutedEventArgs e)
    {
        var asset = _current; if (asset is null) return;
        await RunAsync(async token =>
        {
            _updatingList = true;
            try { _assets.Remove(asset); }
            finally { _updatingList = false; }
            _recognized.Remove(asset.WorkingPath); _recognized.Remove(asset.OriginalPath);
            File.Delete(asset.OriginalPath);
            if (asset.WorkingPath != asset.OriginalPath) File.Delete(asset.WorkingPath);
            _current = _assets.FirstOrDefault();
            if (_current is null)
            {
                Preview.SetImage(null); EmptyHint.IsVisible = true; PreviewTitle.Text = "图片预览"; OcrText.Text = ""; CountText.Text = "图片 · 0";
            }
            else { RefreshList(); await ShowPreviewAsync(_current, token); }
            StatusText.Text = "已从处理列表移除";
        }, "正在移除图片…");
    }

    /// <summary>将当前图片向前移动一位，影响多页文档导出顺序。</summary>
    private void MoveUpClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _current is null) return;
        var index = _assets.IndexOf(_current);
        if (index > 0) { _updatingList = true; try { _assets.Move(index, index - 1); } finally { _updatingList = false; } }
    }

    /// <summary>刷新尺寸文本并保留当前选中项。</summary>
    private void RefreshList()
    {
        _updatingList = true;
        try { ImageList.ItemsSource = null; ImageList.ItemsSource = _assets; ImageList.SelectedItem = _current; }
        finally { _updatingList = false; }
        CountText.Text = $"图片 · {_assets.Count}";
        StatusText.Text = "图片已就绪，可裁剪、提取文字或导出";
    }

    /// <summary>切换 PNG 时禁用有损画质选项。</summary>
    private void FormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QualitySlider is not null) QualitySlider.IsEnabled = FormatBox.SelectedIndex != 1;
    }

    /// <summary>读取用户指定目录，未指定时获取主程序下载目录。</summary>
    private async Task<string> OutputDirectoryAsync(CancellationToken token)
    {
        var path = OutputPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) && _drive is not null) path = await _drive.Downloads.GetSaveDirectoryAsync(token);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new IOException("请先选择保存目录。");
        OutputPathBox.Text = Path.GetFullPath(path);
        return OutputPathBox.Text;
    }

    /// <summary>从系统文件夹选择器选择保存目录。</summary>
    private async void ChooseOutputClick(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async token =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择保存目录", AllowMultiple = false });
            token.ThrowIfCancellationRequested();
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string path) OutputPathBox.Text = path;
        }, "选择保存目录…");
    }

    /// <summary>读取当前导出范围，不改变图片排序。</summary>
    private ImageAsset[] ExportSelection()
    {
        return ScopeBox.SelectedIndex == 1 ? (_current is null ? [] : [_current]) : _assets.ToArray();
    }

    /// <summary>按格式、画质和最长边设置批量导出图片。</summary>
    private async void ExportImagesClick(object? sender, RoutedEventArgs e)
    {
        var assets = ExportSelection(); if (assets.Length == 0) return;
        var format = FormatBox.SelectedIndex switch { 1 => "png", 2 => "webp", _ => "jpg" };
        var quality = (int)QualitySlider.Value; var maxSide = (int)(MaxSideBox.Value ?? 0);
        await RunAsync(async token =>
        {
            var directory = await OutputDirectoryAsync(token);
            long bytes = 0;
            var count = 0;
            foreach (var asset in assets)
            {
                StatusText.Text = $"正在导出 {++count}/{assets.Length}：{asset.Name}";
                var path = await Task.Run(() => ImageProcessor.SaveOutput(directory, asset.Name, "." + format, temporary =>
                {
                    using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
                    using var resized = ImageProcessor.Resize(bitmap, maxSide);
                    File.WriteAllBytes(temporary, ImageProcessor.Encode(resized, format, quality));
                }, token), token);
                bytes += new FileInfo(path).Length;
            }
            StatusText.Text = $"已保存 {assets.Length} 张图片 · 共 {bytes / 1024d:N1} KiB；压缩后体积取决于原格式和画质";
            _drive?.Logger.Info(StatusText.Text);
        }, "正在导出图片…");
    }

    /// <summary>导出选中范围的 PDF。</summary>
    private async void ExportPdfClick(object? sender, RoutedEventArgs e) { await ExportDocumentAsync("pdf"); }
    /// <summary>导出选中范围的 Word 文档。</summary>
    private async void ExportWordClick(object? sender, RoutedEventArgs e) { await ExportDocumentAsync("docx"); }
    /// <summary>导出选中范围的 Excel 工作簿。</summary>
    private async void ExportExcelClick(object? sender, RoutedEventArgs e) { await ExportDocumentAsync("xlsx"); }

    /// <summary>串行生成标准文档，使用临时提交避免取消时留下损坏文件。</summary>
    private async Task ExportDocumentAsync(string format)
    {
        var assets = ExportSelection(); if (assets.Length == 0) return;
        var recognize = OfficeMode.SelectedIndex == 1 && format != "pdf";
        await RunAsync(async token =>
        {
            var directory = await OutputDirectoryAsync(token);
            var progress = MakeOcrProgress();
            var output = await Task.Run(() => ImageProcessor.SaveOutput(directory,
                assets.Length == 1 ? assets[0].Name : "图片合集", "." + format, temporary =>
                {
                    RecognizedLine[] Text(ImageAsset asset)
                    {
                        return Recognize(asset, progress, token);
                    }
                    if (format == "pdf") DocumentExporter.Pdf(temporary, assets, token);
                    else if (format == "docx") DocumentExporter.Word(temporary, assets, recognize, Text, token);
                    else DocumentExporter.Excel(temporary, assets, recognize, Text, token);
                }, token), token);
            if (_current is not null && _recognized.TryGetValue(_current.WorkingPath, out var lines)) OcrText.Text = TextOf(lines);
            StatusText.Text = "已保存：" + output; _drive?.Logger.Info("图片处理导出成功：" + output);
        }, recognize ? "正在本地识别并生成文档…" : "正在生成文档…");
    }

    /// <summary>复用当前编辑版本的识别结果，避免同一张图反复运行模型。</summary>
    private RecognizedLine[] Recognize(ImageAsset asset, IProgress<(int Completed, int Total)> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_recognized.TryGetValue(asset.WorkingPath, out var lines)) return lines;
        using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
        lines = _ocr.Recognize(bitmap, progress, token);
        token.ThrowIfCancellationRequested();
        _recognized[asset.WorkingPath] = lines;
        return lines;
    }

    /// <summary>创建带任务版本检查的 UI 进度回调，旧任务不能覆盖新任务状态。</summary>
    private IProgress<(int Completed, int Total)> MakeOcrProgress()
    {
        var version = _jobVersion;
        return new Progress<(int Completed, int Total)>(p =>
        {
            if (!_closed && _busy && version == _jobVersion) StatusText.Text = $"正在本地识别文字 · {p.Completed}/{p.Total} 段";
        });
    }

    /// <summary>提取当前图片文字并显示可复制的识别结果。</summary>
    private async void OcrClick(object? sender, RoutedEventArgs e)
    {
        var asset = _current; if (asset is null) return;
        await RunAsync(async token =>
        {
            var progress = MakeOcrProgress();
            var lines = await Task.Run(() => Recognize(asset, progress, token), token);
            OcrText.Text = TextOf(lines);
            StatusText.Text = lines.Length > 0 ? $"识别完成 · {lines.Length} 段文字" : "未识别到文字，可尝试裁剪文字区域后重试";
        }, "正在本地识别中英文，首次使用需要加载模型…");
    }

    /// <summary>按阅读顺序合并识别文字。</summary>
    private static string TextOf(RecognizedLine[] lines)
    {
        return string.Join(Environment.NewLine, OcrService.GroupRows(lines).Select(row => string.Join("  ", row.Select(line => line.Text))));
    }

    /// <summary>把当前识别结果复制到系统剪贴板。</summary>
    private async void CopyTextClick(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async token =>
        {
            if (Clipboard is null) throw new IOException("当前环境不支持剪贴板。");
            await Clipboard.SetTextAsync(OcrText.Text ?? ""); StatusText.Text = "文字已复制";
        }, "正在复制文字…");
    }

    /// <summary>将当前识别结果保存为 UTF-8 文本。</summary>
    private async void SaveTextClick(object? sender, RoutedEventArgs e)
    {
        var text = OcrText.Text; var asset = _current;
        if (string.IsNullOrEmpty(text) || asset is null) return;
        await RunAsync(async token =>
        {
            var directory = await OutputDirectoryAsync(token);
            var path = ImageProcessor.SaveOutput(directory, asset.Name, ".txt",
                temporary => File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false)), token);
            StatusText.Text = "已保存：" + path;
        }, "正在保存文字…");
    }

    /// <summary>打开云端选择器，从主程序当前目录开始浏览。</summary>
    private async void CloudClick(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async token =>
        {
            if (_drive is null) throw new IOException("当前窗口没有连接网盘。");
            if (!(await _drive.User.GetCurrentAsync(token)).IsLoggedIn) throw new IOException("选择云端图片前请先登录网盘。");
            _cloudHistory.Clear(); _cloudFolder = await _drive.Files.GetCurrentFolderAsync(token);
            _cloudTitle = "当前网盘目录"; _cloudPage = 1; CloudOverlay.IsVisible = true;
            await LoadCloudAsync(token);
        }, "正在读取网盘目录…");
    }

    /// <summary>分页读取云端目录，保留文件夹以支持逐层浏览。</summary>
    private async Task LoadCloudAsync(CancellationToken token)
    {
        var page = await _drive!.Files.ListAsync(_cloudFolder, _cloudPage, 200, token);
        token.ThrowIfCancellationRequested();
        CloudList.ItemsSource = page.Folders.Concat(page.Files.Where(file => IsSupported(file.Name))).ToArray();
        CloudLocation.Text = $"{_cloudTitle} · 第 {_cloudPage} 页";
        CloudHelp.Text = "双击文件夹进入，选择图片后添加。";
        CloudPrevious.IsEnabled = _cloudPage > 1; CloudNext.IsEnabled = _cloudPage * 200 < page.TotalFiles;
        CloudBack.IsEnabled = _cloudHistory.Count > 0;
    }

    /// <summary>云端选择器里双击文件夹进入；双击图片不自动添加或处理。</summary>
    private async void CloudDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_busy) return;
        var control = e.Source as Control;
        var file = control?.DataContext as FileEntry ??
            control?.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<FileEntry>().FirstOrDefault();
        if (file is not { IsFolder: true }) return;
        await RunAsync(async token =>
        {
            _cloudHistory.Push((_cloudFolder, _cloudTitle)); _cloudFolder = file.Id; _cloudTitle = file.Name; _cloudPage = 1;
            await LoadCloudAsync(token);
        }, "正在读取文件夹…");
    }

    /// <summary>返回云端选择器上一次进入的目录。</summary>
    private async void CloudBackClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _cloudHistory.Count == 0) return;
        await RunAsync(async token => { (_cloudFolder, _cloudTitle) = _cloudHistory.Pop(); _cloudPage = 1; await LoadCloudAsync(token); }, "读取上级目录…");
    }
    /// <summary>跳转到登录账户的根目录。</summary>
    private async void CloudRootClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunAsync(async token => { _cloudHistory.Clear(); _cloudFolder = ""; _cloudTitle = "根目录"; _cloudPage = 1; await LoadCloudAsync(token); }, "读取根目录…");
    }
    /// <summary>读取下一页云端文件。</summary>
    private async void CloudNextClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunAsync(async token => { _cloudPage++; await LoadCloudAsync(token); }, "正在翻页…");
    }
    /// <summary>读取上一页云端文件。</summary>
    private async void CloudPreviousClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _cloudPage <= 1) return;
        await RunAsync(async token => { _cloudPage--; await LoadCloudAsync(token); }, "正在翻页…");
    }
    /// <summary>只添加明确选中的云端图片，目录本身不作为图片导入。</summary>
    private async void AddCloudClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var files = CloudList.SelectedItems?.OfType<FileEntry>().Where(file => !file.IsFolder).ToArray() ?? [];
        if (files.Length == 0) { CloudHelp.Text = "请先选中至少一张图片。"; return; }
        CloudOverlay.IsVisible = false; await AddCloudFilesAsync(files);
    }
    /// <summary>关闭云端选择器并取消仍在进行的目录读取。</summary>
    private void CloseCloudClick(object? sender, RoutedEventArgs e)
    {
        _operation?.Cancel(); CloudOverlay.IsVisible = false;
    }
    /// <summary>取消当前操作。</summary>
    private void CancelClick(object? sender, RoutedEventArgs e) { _operation?.Cancel(); }

    /// <summary>先取消并等待旧任务退出，再启动新任务，避免同时修改图片和模型。</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action, string status)
    {
        var version = ++_jobVersion; _operation?.Cancel(); await _job;
        if (_closed || _lifetime.IsCancellationRequested || version != _jobVersion) return;
        _job = RunCoreAsync(action, status); await _job;
    }

    /// <summary>统一处理取消、异常、按钮状态和日志，异步事件不向运行器泄漏异常。</summary>
    private async Task RunCoreAsync(Func<CancellationToken, Task> action, string status)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation; SetBusy(true); StatusText.ClearValue(TextBlock.ForegroundProperty); StatusText.Text = status;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "已取消，已导出的文件保留"; }
        catch (Exception error)
        {
            if (!_closed)
            {
                StatusText.Text = error is HttpRequestException ? "图片读取失败，请检查网络后重新添加。" : error.Message;
                StatusText.Foreground = this.FindResource("ErrorColor") as IBrush;
                if (CloudOverlay.IsVisible) CloudHelp.Text = StatusText.Text;
                _drive?.Logger.Error("图片处理失败：" + error);
            }
        }
        finally { _operation = null; if (!_closed) SetBusy(false); }
    }

    /// <summary>耗时操作时禁用冲突控件，保留取消和窗口关闭。</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        SourceButtons.IsEnabled = !busy; ImageList.IsEnabled = !busy; Preview.IsEnabled = !busy;
        ToolsPanel.IsEnabled = !busy && _assets.Count > 0;
        RemoveButton.IsEnabled = !busy && _current is not null;
        MoveUpButton.IsEnabled = !busy && _current is not null && _assets.IndexOf(_current) > 0;
        ChooseOutputButton.IsEnabled = !busy; OutputPathBox.IsEnabled = !busy; CloudList.IsEnabled = !busy;
        CopyTextButton.IsEnabled = SaveTextButton.IsEnabled = !busy && !string.IsNullOrEmpty(OcrText.Text);
        CancelButton.IsEnabled = busy; WorkProgress.IsVisible = busy;
    }

    /// <summary>取消并等待后台任务结束，然后释放模型、预览、连接及本窗口独占的临时目录。</summary>
    private async void WindowClosed(object? sender, EventArgs e)
    {
        _closed = true; _lifetime.Cancel(); await _job;
        Preview.Dispose(); _ocr.Dispose(); _http.Dispose(); _lifetime.Dispose();
        try { Directory.Delete(_temporary, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>离线验证时导入指定测试图片，不调用宿主 API。</summary>
    internal async Task LoadPreviewFixtureAsync(string path)
    {
        await RunAsync(async token =>
        {
            var target = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".input");
            await using (var stream = File.OpenRead(path)) await ImageProcessor.CopyInputAsync(stream, target, token);
            await ImportAsync(target, "图片识别示例.png", "本地", token);
        }, "生成预览…");
    }
}
