using Drive.Plugin.Abi;
using Drive.Plugin.Avalonia;
using Drive.Plugin.SDK;

namespace PortraitRetouchPlugin;

/// <summary>人像精修入口，仅注册图片文件菜单和插件应用入口。</summary>
[DrivePlugin(Id = "com.drive.plugins.portrait-retouch", Name = "人像精修", Author = "Drive", Version = "1.0.0",
    Description = "本地人像定位、自然美颜、局部修复与光影调色，保留原图并支持前后对比。",
    IconBase64 = IconData.Ico, BackgroundColor = "#A38551", Tags = new[] { "人像美颜", "图片精修", "本地AI" },
    Capabilities = PluginCapabilities.HasUi | PluginCapabilities.WindowsUi | PluginCapabilities.LinuxUi | PluginCapabilities.MacOsUi)]
[PluginPermission(PluginPermission.FileRead | PluginPermission.UserRead | PluginPermission.DownloadRead | PluginPermission.UiFileMenu | PluginPermission.UiApplication)]
public sealed class Plugin : DrivePlugin
{
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];
    private PluginWindowHost? _windows;
    private CancellationTokenSource? _enabled;
    /// <summary>创建窗口管理器并订阅菜单及账户变化。</summary>
    protected override void OnLoad()
    {
        _windows = new(Drive); Drive.Events.ActionInvoked += ActionInvoked; Drive.Events.UserChanged += UserChanged;
    }
    /// <summary>启用后申请人像精修图片菜单。</summary>
    protected override void OnEnable()
    {
        _enabled = new(); _ = RegisterAsync(_enabled.Token);
    }
    /// <summary>注册菜单，失败时输出可追踪日志。</summary>
    private async Task RegisterAsync(CancellationToken token)
    {
        try { await Drive.UI.RegisterFileMenuAsync(new PluginAction { Id = "portrait-retouch", Title = "人像精修", Extensions = Extensions }, token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("人像精修菜单注册失败：" + error.Message); }
    }
    /// <summary>从插件应用卡片打开工作台。</summary>
    protected override void OnAppActivated() { _ = ShowAsync(null); }
    /// <summary>从图片菜单打开第一张受支持图片，不订阅 FilePage 双击。</summary>
    private void ActionInvoked(object? sender, DriveEvent data)
    {
        if (data.ActionId != "portrait-retouch") return;
        _ = ShowAsync(data.Selection.FirstOrDefault(file => !file.IsFolder && Supports(file.Name)));
    }
    /// <summary>复用工作台，在宿主前居中显示，再导入选中图片。</summary>
    private async Task ShowAsync(FileEntry? file)
    {
        var enabled = _enabled; if (enabled is null || enabled.IsCancellationRequested) return;
        try
        {
            PortraitWindow? window = null;
            await _windows!.ShowNearHostAsync(() => window = new PortraitWindow(Drive, enabled.Token),
                existing => window = (PortraitWindow)existing, enabled.Token);
            if (window is not null && file is not null) await window.OpenCloudFileAsync(file);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("打开人像精修失败：" + error.Message); }
    }
    /// <summary>判断图片后缀，不区分大小写。</summary>
    internal static bool Supports(string name) { return Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase); }
    /// <summary>切换账户时关闭旧图片和数据读取任务。</summary>
    private void UserChanged(object? sender, DriveEvent data) { _windows?.CloseWindow(); }
    /// <summary>停用时取消任务并关闭界面。</summary>
    protected override void OnDisable()
    {
        _enabled?.Cancel(); _windows?.CloseWindow(); _enabled?.Dispose(); _enabled = null;
    }
    /// <summary>运行器结束前释放本轮插件生命周期。</summary>
    protected override void OnShutdown() { OnDisable(); }
}
