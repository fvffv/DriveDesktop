using Drive.Plugin.Abi;
using Drive.Plugin.Avalonia;
using Drive.Plugin.SDK;

namespace ImageToolsPlugin;

/// <summary>图片处理插件入口：仅从图片文件菜单或插件卡片打开，不订阅文件双击。</summary>
[DrivePlugin(Id = "com.drive.plugins.image-tools", Name = "图片处理", Author = "Drive", Version = "1.0.0",
    Description = "本地与云端图片裁剪、压缩、格式转换、中英文识别，以及 PDF、Word、Excel 导出。",
    IconBase64 = IconData.Ico, BackgroundColor = "#3B82F6", Tags = new[] { "图片工具", "文字识别", "文档转换" },
    Capabilities = PluginCapabilities.HasUi | PluginCapabilities.WindowsUi | PluginCapabilities.LinuxUi | PluginCapabilities.MacOsUi)]
[PluginPermission(PluginPermission.FileRead | PluginPermission.DownloadRead | PluginPermission.UserRead |
    PluginPermission.UiFileMenu | PluginPermission.UiApplication)]
public sealed class Plugin : DrivePlugin
{
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];
    private PluginWindowHost? _windows;
    private CancellationTokenSource? _enabled;

    /// <summary>初始化窗口管理器，只订阅本插件菜单和账户切换。</summary>
    protected override void OnLoad()
    {
        _windows = new PluginWindowHost(Drive);
        Drive.Events.ActionInvoked += OnActionInvoked;
        Drive.Events.UserChanged += OnUserChanged;
    }

    /// <summary>启用时注册图片文件菜单。</summary>
    protected override void OnEnable()
    {
        _enabled = new CancellationTokenSource();
        _ = RegisterMenuAsync(_enabled.Token);
    }

    /// <summary>申请仅出现在受支持图片上的菜单；注册失败写入宿主日志。</summary>
    private async Task RegisterMenuAsync(CancellationToken token)
    {
        try { await Drive.UI.RegisterFileMenuAsync(new PluginAction { Id = "image-tools", Title = "图片处理", Extensions = Extensions }, token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("图片处理菜单注册失败：" + error.Message); }
    }

    /// <summary>插件卡片打开空窗口，用户可主动选择云端或本地图片。</summary>
    protected override void OnAppActivated()
    {
        _ = ShowAsync([]);
    }

    /// <summary>从图片菜单获取文件快照并打开处理窗口。</summary>
    private void OnActionInvoked(object? sender, DriveEvent data)
    {
        if (data.ActionId != "image-tools") return;
        _ = ShowAsync(data.Selection.Where(file => !file.IsFolder &&
            Extensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase)).ToArray());
    }

    /// <summary>在宿主窗口前居中显示插件，复用现有窗口并添加选中的云端图片。</summary>
    private async Task ShowAsync(FileEntry[] files)
    {
        var enabled = _enabled;
        if (enabled is null || enabled.IsCancellationRequested) return;
        try
        {
            ImageToolsWindow? window = null;
            await _windows!.ShowNearHostAsync(() => window = new ImageToolsWindow(Drive, enabled.Token),
                existing => window = (ImageToolsWindow)existing, enabled.Token);
            if (window is not null && files.Length > 0) await window.AddCloudFilesAsync(files);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("打开图片处理窗口失败：" + error.Message); }
    }

    /// <summary>账户变化时关闭窗口，取消旧账户的数据读取。</summary>
    private void OnUserChanged(object? sender, DriveEvent data)
    {
        _windows?.CloseWindow();
    }

    /// <summary>停用时取消本轮任务并关闭窗口；宿主自动移除菜单。</summary>
    protected override void OnDisable()
    {
        _enabled?.Cancel(); _windows?.CloseWindow();
        _enabled?.Dispose(); _enabled = null;
    }

    /// <summary>退出时关闭插件界面及本轮生命周期。</summary>
    protected override void OnShutdown()
    {
        OnDisable();
    }
}
