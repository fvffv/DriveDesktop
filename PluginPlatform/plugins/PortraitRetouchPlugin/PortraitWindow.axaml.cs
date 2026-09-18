using System.ComponentModel;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Drive.Plugin.SDK;
using ImageToolsPlugin.Core;
using PortraitRetouchPlugin.Core;
using SkiaSharp;

namespace PortraitRetouchPlugin;

/// <summary>预设缩略图及其选中状态，缩略图由真实精修算法生成。</summary>
internal sealed class PresetChoice(string name, Bitmap thumbnail) : INotifyPropertyChanged, IDisposable
{
    private bool _selected;
    /// <summary>预设名称。</summary>
    public string Name { get; } = name;
    /// <summary>预设效果预览。</summary>
    public Bitmap Thumbnail { get; } = thumbnail;
    /// <summary>当前参数是否与此预设匹配。</summary>
    public bool Selected { get { return _selected; } set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    /// <summary>通知界面更新预设状态。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>释放缩略图。</summary>
    public void Dispose() { Thumbnail.Dispose(); }
}

/// <summary>独立人像精修工作台，所有预览都从原图重算，文件读写及模型推理在后台执行。</summary>
public sealed partial class PortraitWindow : Window
{
    private readonly PluginContext? _drive;
    private readonly CancellationTokenSource _lifetime;
    private readonly FaceDetector _detector = new();
    private readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) });
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "DrivePortrait", Guid.NewGuid().ToString("N"));
    private readonly List<EditSnapshot> _history = new();
    private readonly List<PresetChoice> _presets = new();
    private readonly Stack<(string Id, string Name)> _cloudHistory = new();
    private CancellationTokenSource? _operation, _renderCancellation;
    private Task _busyTask = Task.CompletedTask, _renderTask = Task.CompletedTask;
    private SKBitmap? _sourcePreview;
    private FaceRegion[] _faces = [];
    private EditSnapshot _state = new(new(), []);
    private EditSnapshot? _displayedState;
    private string? _sourcePath, _pendingInput;
    private string _name = "人像", _cloudFolder = "", _cloudTitle = "根目录", _lastEditKey = "";
    private DateTime _lastEditTime;
    private int _sourceWidth, _sourceHeight, _historyIndex, _renderVersion, _busyVersion, _cloudPage = 1;
    private bool _initialized, _suppress, _busy, _closed;

    /// <summary>设计器构造函数，不连接网盘。</summary>
    public PortraitWindow() : this(null, CancellationToken.None) { }
    /// <summary>创建精修窗口并接入插件生命周期。</summary>
    public PortraitWindow(PluginContext? drive, CancellationToken enabled)
    {
        _drive = drive; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(enabled);
        Directory.CreateDirectory(_temporary); InitializeComponent();
        Canvas.StrokeCompleted += StrokeCompleted; Canvas.ZoomChanged += CanvasZoomChanged;
        Closed += WindowClosed; KeyUp += WindowKeyUp;
        _initialized = true; ApplyState(); SetBusy(false);
        if (IconData.Ico.Length > 0) Icon = new WindowIcon(new MemoryStream(Convert.FromBase64String(IconData.Ico)));
        var screen = Screens.Primary;
        if (screen is not null)
        {
            Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling - 24);
            Height = Math.Min(Height, screen.WorkingArea.Height / screen.Scaling - 48);
        }
    }

    /// <summary>打开图片菜单传入的云端文件，单张精修不隐式批量修改其他图片。</summary>
    public Task OpenCloudFileAsync(FileEntry file)
    {
        return RunBusyAsync(async token =>
        {
            if (_drive is null) throw new IOException("窗口未连接网盘。");
            if (file.IsFolder || !Plugin.Supports(file.Name)) throw new IOException("请选择支持的图片文件。");
            var fresh = await _drive.Files.GetAsync(file.Id, token);
            if (fresh.Size > (ulong)ImageProcessor.MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
            var target = NewInputPath();
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(token);
            transfer.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var link = await _drive.Files.GetDownloadUrlAsync(file.Id, transfer.Token);
                    if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new IOException("网盘返回的图片链接无效。");
                    using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, transfer.Token);
                    var expired = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.Gone ||
                        response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json";
                    if (expired && attempt == 0) continue;
                    response.EnsureSuccessStatusCode();
                    if (expired) throw new IOException("临时链接已失效，请重新打开。");
                    if (response.Content.Headers.ContentLength > ImageProcessor.MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
                    await using var input = await response.Content.ReadAsStreamAsync(transfer.Token);
                    await ImageProcessor.CopyInputAsync(input, target, transfer.Token); break;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("云端图片读取超时，请重试。"); }
            await ImportAsync(target, file.Name, token);
        }, "正在读取云端人像…");
    }

    /// <summary>选择一张本地图片，在独占临时目录中保留只读副本。</summary>
    private async void LocalClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开人像图片", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("图片") { Patterns = Plugin.Extensions.Select(x => "*" + x).ToArray() }] });
            token.ThrowIfCancellationRequested();
            if (files.FirstOrDefault() is not { } file) { StatusText.Text = "未选择图片"; return; }
            var target = NewInputPath();
            await using (var input = await file.OpenReadAsync()) await ImageProcessor.CopyInputAsync(input, target, token);
            await ImportAsync(target, file.Name, token);
        }, "选择本地图片…");
    }
    /// <summary>载入插件内置的虚构示例人物，供用户离线试用。</summary>
    private async void DemoClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var target = NewInputPath();
            await using (var input = AssetLoader.Open(new Uri("avares://PortraitRetouchPlugin/Assets/demo-portrait.png")))
                await ImageProcessor.CopyInputAsync(input, target, token);
            await ImportAsync(target, "示例人像（AI生成）.png", token);
        }, "正在打开示例人像…");
    }
    /// <summary>创建本窗口独占的输入路径。</summary>
    private string NewInputPath()
    {
        _pendingInput = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".input");
        return _pendingInput;
    }

    /// <summary>验证并分析新图片；成功后才替换当前编辑会话。</summary>
    private async Task ImportAsync(string path, string name, CancellationToken token)
    {
        StatusText.Text = "正在定位人脸与生成预览…";
        var result = await Task.Run(() =>
        {
            using var bitmap = ImageProcessor.Decode(path);
            token.ThrowIfCancellationRequested();
            FaceRegion[] faces; string? detectionError = null;
            try { faces = _detector.Detect(bitmap, token); }
            catch (Exception error) when (!token.IsCancellationRequested) { faces = []; detectionError = error.Message; }
            var preview = ImageProcessor.Resize(bitmap, 1400);
            try
            {
                using var small = ImageProcessor.Resize(bitmap, 240);
                var thumbs = new List<(string Name, byte[] Bytes)>();
                foreach (var preset in new[] { "原图", "自然", "清透", "暖肤", "质感" })
                {
                    using var sample = RetouchEngine.Render(small, faces, RetouchSettings.Preset(preset), [], token);
                    thumbs.Add((preset, ImageProcessor.Encode(sample, "jpg", 85)));
                }
                return (Preview: preview, Faces: faces, Width: bitmap.Width, Height: bitmap.Height,
                    Before: ImageProcessor.Encode(preview, "png", 100), Thumbs: thumbs, Error: detectionError);
            }
            catch { preview.Dispose(); throw; }
        }, token);
        if (token.IsCancellationRequested) { result.Preview.Dispose(); token.ThrowIfCancellationRequested(); }
        _sourcePreview?.Dispose(); _sourcePreview = result.Preview; _faces = result.Faces;
        var priorPath = _sourcePath; _sourcePath = path; _name = name; _sourceWidth = result.Width; _sourceHeight = result.Height;
        if (priorPath is not null && priorPath != path) { try { File.Delete(priorPath); } catch (IOException) { } }
        Canvas.SetSource(new Bitmap(new MemoryStream(result.Before))); EmptyHint.IsVisible = false;
        FileTitle.Text = $"{name} · {_sourceWidth} × {_sourceHeight}";
        foreach (var preset in _presets) preset.Dispose(); _presets.Clear();
        foreach (var thumb in result.Thumbs) _presets.Add(new(thumb.Name, new Bitmap(new MemoryStream(thumb.Bytes))));
        PresetList.ItemsSource = null; PresetList.ItemsSource = _presets;
        _suppress = true;
        try { FaceChoice.ItemsSource = new[] { "全部已定位人脸" }.Concat(_faces.Select((face, index) => $"人脸 {index + 1} · 置信度 {face.Score:P0}")).ToArray(); FaceChoice.SelectedIndex = 0; }
        finally { _suppress = false; }
        FaceStatus.Text = _faces.Length > 0 ? $"已定位 {_faces.Length} 张人脸" : "未定位到人脸";
        _state = new(new(), []); _displayedState = _state;
        _history.Clear(); _history.Add(_state); _historyIndex = 0; _lastEditKey = "";
        ApplyState();
        StatusText.Text = result.Error is not null ? "人脸定位不可用，可继续调色与局部修复" :
            _faces.Length > 0 ? "人像已就绪 · 选择预设或调节参数开始精修" : "未检测到人脸，可调色或局部修复；请尝试正面、清晰的人像";
        if (result.Error is not null) _drive?.Logger.Warning("人像定位失败：" + result.Error);
        _drive?.Logger.Info($"人像已打开：{name}；检测到 {_faces.Length} 张人脸。");
    }

    /// <summary>读出当前全部调节值，生成独立参数快照。</summary>
    private RetouchSettings ReadSettings()
    {
        return new() { Smooth = SmoothSlider.Value, Whiten = WhitenSlider.Value, Blush = BlushSlider.Value,
            Slim = SlimSlider.Value, Eyes = EyesSlider.Value, EyeLight = EyeLightSlider.Value, Teeth = TeethSlider.Value,
            Exposure = ExposureSlider.Value, Contrast = ContrastSlider.Value, Highlights = HighlightsSlider.Value,
            Shadows = ShadowsSlider.Value, Saturation = SaturationSlider.Value, Warmth = WarmthSlider.Value,
            Sharpness = SharpnessSlider.Value, Vignette = VignetteSlider.Value, FaceIndex = FaceChoice.SelectedIndex - 1 };
    }
    /// <summary>把当前编辑记录恢复到控件，不产生新的编辑历史。</summary>
    private void ApplyState()
    {
        _suppress = true;
        try
        {
            var s = _state.Settings;
            SmoothSlider.Value = s.Smooth; WhitenSlider.Value = s.Whiten; BlushSlider.Value = s.Blush;
            SlimSlider.Value = s.Slim; EyesSlider.Value = s.Eyes; EyeLightSlider.Value = s.EyeLight; TeethSlider.Value = s.Teeth;
            ExposureSlider.Value = s.Exposure; ContrastSlider.Value = s.Contrast; HighlightsSlider.Value = s.Highlights;
            ShadowsSlider.Value = s.Shadows; SaturationSlider.Value = s.Saturation; WarmthSlider.Value = s.Warmth;
            SharpnessSlider.Value = s.Sharpness; VignetteSlider.Value = s.Vignette;
            FaceChoice.SelectedIndex = s.FaceIndex + 1;
            foreach (var preset in _presets) preset.Selected = s == RetouchSettings.Preset(preset.Name) with { FaceIndex = s.FaceIndex };
        }
        finally { _suppress = false; }
        UpdateHistoryButtons(); UpdateBrush();
    }
    /// <summary>提交编辑记录；同一个滑块的连续拖动合并为一步，最多保留 60 步。</summary>
    private void Commit(EditSnapshot state, string key)
    {
        if (_state.Settings == state.Settings && _state.Spots.SequenceEqual(state.Spots)) return;
        var now = DateTime.UtcNow;
        if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        var merge = key.StartsWith("slider:", StringComparison.Ordinal) && key == _lastEditKey &&
            (now - _lastEditTime).TotalMilliseconds < 650 && _historyIndex > 0;
        _state = state;
        if (merge) _history[_historyIndex] = state;
        else { _history.Add(state); _historyIndex++; }
        if (_history.Count > 60) { _history.RemoveAt(0); _historyIndex--; }
        _lastEditKey = key; _lastEditTime = now;
        ApplyState(); QueuePreview();
    }
    /// <summary>调节滑块后刷新参数，后台预览会合并快速连续的变化。</summary>
    private void AdjustmentChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_initialized || _suppress || _sourcePreview is null) return;
        Commit(new(ReadSettings(), _state.Spots), "slider:" + (sender as Control)?.Name);
    }
    /// <summary>选择仅美化指定人脸，调色仍作用于全图。</summary>
    private void FaceChoiceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _suppress || _sourcePreview is null) return;
        Commit(new(ReadSettings(), _state.Spots), "face");
    }
    /// <summary>应用真实效果预设，保留已经完成的修复笔触。</summary>
    private void PresetClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Control { DataContext: PresetChoice choice }) return;
        Commit(new(RetouchSettings.Preset(choice.Name) with { FaceIndex = _state.Settings.FaceIndex }, _state.Spots), "preset");
        ApplyState();
    }
    /// <summary>合并一次画笔操作到可撤销记录，限制笔触总数。</summary>
    private void StrokeCompleted(object? sender, HealSpot[] spots)
    {
        if (_busy || _sourcePreview is null) return;
        if (_state.Spots.Length + spots.Length > 2000) { StatusText.Text = "本张图片已达到修复点上限，可撤销部分笔触后继续"; return; }
        Commit(new(_state.Settings, _state.Spots.Concat(spots).ToArray()), "brush");
    }
    /// <summary>启停修复画笔时显示完整效果，避免在原图一侧误操作。</summary>
    private void HealChanged(object? sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        Canvas.Brush = HealToggle.IsChecked == true;
        if (Canvas.Brush) { OriginalToggle.IsChecked = false; CompareToggle.IsChecked = false; Canvas.Original = Canvas.Compare = false; CompareLabels.IsVisible = false; }
        UpdateBrush();
    }
    /// <summary>更新原图像素单位的画笔大小。</summary>
    private void BrushSizeChanged(object? sender, RangeBaseValueChangedEventArgs e) { if (_initialized) UpdateBrush(); }
    /// <summary>把画笔直径转换为短边归一化半径。</summary>
    private void UpdateBrush() { Canvas.BrushRadius = (float)BrushSize.Value / 2 / Math.Max(1, Math.Min(_sourceWidth, _sourceHeight)); }
    /// <summary>撤销一步，参数和修复笔触一起恢复。</summary>
    private void UndoClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _historyIndex <= 0) return;
        _state = _history[--_historyIndex]; _lastEditKey = ""; ApplyState(); QueuePreview();
    }
    /// <summary>重做被撤销的编辑。</summary>
    private void RedoClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _historyIndex >= _history.Count - 1) return;
        _state = _history[++_historyIndex]; _lastEditKey = ""; ApplyState(); QueuePreview();
    }
    /// <summary>清除美颜、调色和笔触；重置本身也能撤销。</summary>
    private void ResetClick(object? sender, RoutedEventArgs e) { if (!_busy && _sourcePreview is not null) Commit(new(new(), []), "reset"); }
    /// <summary>更新撤销重做按钮。</summary>
    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = !_busy && _historyIndex > 0;
        RedoButton.IsEnabled = !_busy && _historyIndex < _history.Count - 1;
        ResetButton.IsEnabled = !_busy && _sourcePreview is not null;
    }

    /// <summary>取消旧预览，等待其退出后执行最新参数，避免原图缓冲被并发访问或释放。</summary>
    private void QueuePreview()
    {
        if (_closed || _busy || _sourcePreview is null) return;
        _renderCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _renderCancellation = cancellation;
        var version = ++_renderVersion; var preceding = _renderTask; var state = _state;
        _renderTask = RenderPreviewAsync(preceding, version, state, cancellation);
    }
    /// <summary>在后台生成最新效果；已取消或已过期的结果不会覆盖新预览。</summary>
    private async Task RenderPreviewAsync(Task preceding, int version, EditSnapshot state, CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            try
            {
                await preceding; await Task.Delay(140, cancellation.Token);
                if (_sourcePreview is null) return;
                WorkProgress.IsVisible = true; StatusText.Text = "正在生成精修预览…";
                var source = _sourcePreview; var faces = _faces;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var bytes = await Task.Run(() =>
                {
                    using var output = RetouchEngine.Render(source, faces, state.Settings, state.Spots, cancellation.Token);
                    return ImageProcessor.Encode(output, "png", 100);
                }, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!_closed && version == _renderVersion)
                {
                    Canvas.SetResult(new Bitmap(new MemoryStream(bytes)));
                    _displayedState = state;
                    StatusText.Text = $"预览已更新 · {watch.Elapsed.TotalSeconds:N1} 秒 · 导出使用原图尺寸";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_closed) Report(error); }
            finally
            {
                if (ReferenceEquals(_renderCancellation, cancellation)) _renderCancellation = null;
                if (!_closed && version == _renderVersion && !_busy) WorkProgress.IsVisible = false;
            }
        }
    }

    /// <summary>开启分屏对比；修复画笔会自动关闭。</summary>
    private void CompareClick(object? sender, RoutedEventArgs e)
    {
        HealToggle.IsChecked = false; OriginalToggle.IsChecked = false;
        Canvas.Original = false; Canvas.Compare = CompareToggle.IsChecked == true; CompareLabels.IsVisible = Canvas.Compare;
    }
    /// <summary>切换原图查看状态，不修改编辑参数。</summary>
    private void OriginalClick(object? sender, RoutedEventArgs e)
    {
        HealToggle.IsChecked = false; Canvas.Original = OriginalToggle.IsChecked == true;
        CompareLabels.IsVisible = Canvas.Compare && !Canvas.Original;
    }
    /// <summary>恢复适应窗口。</summary>
    private void FitClick(object? sender, RoutedEventArgs e) { Canvas.Fit(); }
    /// <summary>放大预览。</summary>
    private void ZoomInClick(object? sender, RoutedEventArgs e) { Canvas.ChangeZoom(1.25); }
    /// <summary>缩小预览。</summary>
    private void ZoomOutClick(object? sender, RoutedEventArgs e) { Canvas.ChangeZoom(.8); }
    /// <summary>显示相对于适应窗口的倍率。</summary>
    private void CanvasZoomChanged(object? sender, EventArgs e) { ZoomText.Text = $"{Canvas.Zoom:0.0}×"; }
    /// <summary>提供撤销重做和按住空格看原图快捷键，文本输入时不截获。</summary>
    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || ExportOverlay.IsVisible || CloudOverlay.IsVisible) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { UndoClick(this, new()); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { RedoClick(this, new()); e.Handled = true; }
        else if (e.Key == Key.Space) { Canvas.Original = true; CompareLabels.IsVisible = false; e.Handled = true; }
    }
    /// <summary>松开空格恢复当前对比方式。</summary>
    private void WindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        Canvas.Original = OriginalToggle.IsChecked == true; CompareLabels.IsVisible = Canvas.Compare && !Canvas.Original; e.Handled = true;
    }

    /// <summary>打开导出设置，并读取主程序配置的默认保存目录。</summary>
    private async void OpenExportClick(object? sender, RoutedEventArgs e)
    {
        if (_sourcePath is null) return;
        await RunBusyAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory.Text) && _drive is not null)
                OutputDirectory.Text = await _drive.Downloads.GetSaveDirectoryAsync(token);
            ExportInfo.Text = $"{_name} · {_sourceWidth} × {_sourceHeight} 像素";
            ExportOverlay.IsVisible = true;
        }, "准备导出…");
    }
    /// <summary>关闭导出设置并取消正在进行的导出。</summary>
    private void CloseExportClick(object? sender, RoutedEventArgs e) { _operation?.Cancel(); ExportOverlay.IsVisible = false; }
    /// <summary>PNG 无损编码不使用有损画质参数。</summary>
    private void FormatChanged(object? sender, SelectionChangedEventArgs e) { if (ExportQuality is not null) ExportQuality.IsEnabled = FormatChoice.SelectedIndex != 1; }
    /// <summary>用系统目录选择器设置输出位置。</summary>
    private async void ChooseOutputClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择作品保存目录", AllowMultiple = false });
            token.ThrowIfCancellationRequested();
            if (selected.FirstOrDefault()?.TryGetLocalPath() is string path) OutputDirectory.Text = path;
        }, "选择保存目录…");
    }
    /// <summary>在原始分辨率应用相同参数，临时写入后提交，不覆盖任何已有文件。</summary>
    private async void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (_sourcePath is null || _busy) return;
        var path = _sourcePath; var name = _name; var state = _state; var faces = _faces;
        var format = FormatChoice.SelectedIndex switch { 1 => "png", 2 => "webp", _ => "jpg" };
        var quality = (int)ExportQuality.Value;
        await RunBusyAsync(async token =>
        {
            var directory = OutputDirectory.Text?.Trim();
            if (string.IsNullOrWhiteSpace(directory) && _drive is not null) directory = await _drive.Downloads.GetSaveDirectoryAsync(token);
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new IOException("请选择有效的保存目录。");
            var saved = await Task.Run(() => ImageProcessor.SaveOutput(directory, ImageProcessor.SafeName(name) + "_精修.png", "." + format, target =>
            {
                using var original = ImageProcessor.Decode(path);
                using var rendered = RetouchEngine.Render(original, faces, state.Settings, state.Spots, token);
                token.ThrowIfCancellationRequested();
                File.WriteAllBytes(target, ImageProcessor.Encode(rendered, format, quality));
            }, token), token);
            ExportOverlay.IsVisible = false; StatusText.Text = "作品已保存：" + saved;
            _drive?.Logger.Info("人像精修导出成功：" + saved);
        }, $"正在按 {_sourceWidth} × {_sourceHeight} 原图尺寸导出…");
    }

    /// <summary>从网盘当前目录开始选择一张云端图片。</summary>
    private async void CloudClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            if (_drive is null) throw new IOException("窗口未连接网盘。");
            if (!(await _drive.User.GetCurrentAsync(token)).IsLoggedIn) throw new IOException("选择云端图片前请先登录网盘。");
            _cloudHistory.Clear(); _cloudFolder = await _drive.Files.GetCurrentFolderAsync(token); _cloudTitle = "当前网盘目录"; _cloudPage = 1;
            CloudOverlay.IsVisible = true; await LoadCloudAsync(token);
        }, "正在读取网盘目录…");
    }
    /// <summary>读取分页文件与文件夹，只显示支持的图片后缀。</summary>
    private async Task LoadCloudAsync(CancellationToken token)
    {
        var page = await _drive!.Files.ListAsync(_cloudFolder, _cloudPage, 200, token);
        token.ThrowIfCancellationRequested();
        CloudList.ItemsSource = page.Folders.Concat(page.Files.Where(file => Plugin.Supports(file.Name))).ToArray();
        CloudLocation.Text = $"{_cloudTitle} · 第 {_cloudPage} 页";
        CloudHelp.Text = "双击文件夹进入，选择一张图片后打开。";
        CloudPrevious.IsEnabled = _cloudPage > 1; CloudNext.IsEnabled = _cloudPage * 200 < page.TotalFiles; CloudBack.IsEnabled = _cloudHistory.Count > 0;
    }
    /// <summary>云端选择器双击文件夹导航，不接管网盘 FilePage 的双击事件。</summary>
    private async void CloudDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_busy) return;
        var control = e.Source as Control;
        var file = control?.DataContext as FileEntry ?? control?.GetVisualAncestors().OfType<Control>().Select(x => x.DataContext).OfType<FileEntry>().FirstOrDefault();
        if (file is not { IsFolder: true }) return;
        await RunBusyAsync(async token => { _cloudHistory.Push((_cloudFolder, _cloudTitle)); _cloudFolder = file.Id; _cloudTitle = file.Name; _cloudPage = 1; await LoadCloudAsync(token); }, "正在读取文件夹…");
    }
    /// <summary>返回之前浏览的文件夹。</summary>
    private async void CloudBackClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _cloudHistory.Count == 0) return;
        await RunBusyAsync(async token => { (_cloudFolder, _cloudTitle) = _cloudHistory.Pop(); _cloudPage = 1; await LoadCloudAsync(token); }, "返回上级目录…");
    }
    /// <summary>返回网盘根目录。</summary>
    private async void CloudRootClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async token => { _cloudHistory.Clear(); _cloudFolder = ""; _cloudTitle = "根目录"; _cloudPage = 1; await LoadCloudAsync(token); }, "正在读取根目录…");
    }
    /// <summary>打开下一页。</summary>
    private async void CloudNextClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async token => { _cloudPage++; await LoadCloudAsync(token); }, "正在翻页…");
    }
    /// <summary>打开上一页。</summary>
    private async void CloudPreviousClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _cloudPage <= 1) return;
        await RunBusyAsync(async token => { _cloudPage--; await LoadCloudAsync(token); }, "正在翻页…");
    }
    /// <summary>打开明确选中的图片，文件夹不会被作为图片下载。</summary>
    private async void ChooseCloudClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || CloudList.SelectedItem is not FileEntry { IsFolder: false } file) { CloudHelp.Text = "请先选择一张图片。"; return; }
        CloudOverlay.IsVisible = false; await OpenCloudFileAsync(file);
    }
    /// <summary>关闭云端选择器，取消未完成的目录读取。</summary>
    private void CloseCloudClick(object? sender, RoutedEventArgs e) { _operation?.Cancel(); CloudOverlay.IsVisible = false; }
    /// <summary>取消导入或导出；原图和已完成的作品不受影响。</summary>
    private void CancelClick(object? sender, RoutedEventArgs e) { _operation?.Cancel(); }

    /// <summary>串行执行文件操作，新操作等待旧任务结束后再访问模型和源图。</summary>
    private async Task RunBusyAsync(Func<CancellationToken, Task> action, string status)
    {
        var version = ++_busyVersion; _operation?.Cancel(); await _busyTask;
        if (_closed || _lifetime.IsCancellationRequested || version != _busyVersion) return;
        _busyTask = RunBusyCoreAsync(action, status); await _busyTask;
    }
    /// <summary>统一处理任务状态、取消与错误，避免异步事件异常退出运行器。</summary>
    private async Task RunBusyCoreAsync(Func<CancellationToken, Task> action, string status)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation; SetBusy(true); _renderCancellation?.Cancel(); await _renderTask;
        StatusText.Text = status;
        try { operation.Token.ThrowIfCancellationRequested(); await action(operation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "操作已取消，原图与已完成的导出保留"; }
        catch (Exception error) { if (!_closed) Report(error); }
        finally
        {
            _operation = null;
            if (_pendingInput is not null && _pendingInput != _sourcePath)
            {
                try { File.Delete(_pendingInput); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _pendingInput = null;
            if (!_closed)
            {
                SetBusy(false);
                if (_sourcePreview is not null && !ReferenceEquals(_displayedState, _state)) QueuePreview();
            }
        }
    }
    /// <summary>耗时文件操作时禁用冲突控件，预览生成期间滑块仍可继续调整。</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        SourceButtons.IsEnabled = !busy; Canvas.IsEnabled = !busy; CanvasTools.IsEnabled = !busy && _sourcePreview is not null;
        EditTabs.IsEnabled = !busy && _sourcePreview is not null; PresetList.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _sourcePreview is not null;
        FaceChoice.IsEnabled = !busy && _faces.Length > 0; CloudList.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy; CancelButton.IsVisible = busy; WorkProgress.IsVisible = busy;
        foreach (var slider in new[] { SmoothSlider, WhitenSlider, BlushSlider, SlimSlider, EyesSlider, EyeLightSlider, TeethSlider }) slider.IsEnabled = _faces.Length > 0;
        UpdateHistoryButtons();
    }
    /// <summary>将可读错误呈现给用户，并记录详细异常供开发者定位。</summary>
    private void Report(Exception error)
    {
        StatusText.Text = error is HttpRequestException ? "图片读取失败，请检查网络后重试。" : error.Message;
        if (CloudOverlay.IsVisible) CloudHelp.Text = StatusText.Text;
        _drive?.Logger.Error("人像精修失败：" + error);
    }
    /// <summary>关闭时取消并等待任务，然后释放位图、模型以及本窗口独占的临时文件。</summary>
    private async void WindowClosed(object? sender, EventArgs e)
    {
        _closed = true; _lifetime.Cancel(); _renderCancellation?.Cancel(); await _busyTask; await _renderTask;
        Canvas.Dispose(); _sourcePreview?.Dispose(); foreach (var preset in _presets) preset.Dispose();
        _detector.Dispose(); _http.Dispose(); _lifetime.Dispose();
        try { Directory.Delete(_temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    /// <summary>离线检查使用的真实导入路径，不连接用户账户。</summary>
    internal Task LoadFixtureAsync(string path)
    {
        return RunBusyAsync(async token =>
        {
            var target = NewInputPath(); await using (var input = File.OpenRead(path)) await ImageProcessor.CopyInputAsync(input, target, token);
            await ImportAsync(target, "示例人像.png", token);
        }, "读取测试图片…");
    }
}
