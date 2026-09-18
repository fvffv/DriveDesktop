using System.Net;
using Drive.Plugin.Abi;
using Drive.Plugin.Avalonia;
using Drive.Plugin.SDK;

namespace ArchivePreviewPlugin;

/// <summary>压缩包预览器入口，通过临时链接按需读取归档，在独立 Avalonia 运行器中展示。</summary>
[DrivePlugin(Id = "com.drive.plugins.archive-preview", Name = "压缩包预览器", Author = "Drive", Version = "1.0.0",
    Description = "无需先下载整包，浏览 ZIP、7z、RAR、TAR，按需提取指定文件。",
    IconBase64 = IconData.Ico, BackgroundColor = "#3B82F6", Tags = new[] { "文件工具", "压缩包", "按需读取" },
    Capabilities = PluginCapabilities.HasUi | PluginCapabilities.WindowsUi | PluginCapabilities.LinuxUi | PluginCapabilities.MacOsUi)]
[PluginPermission(PluginPermission.FileRead | PluginPermission.DownloadRead | PluginPermission.UserRead |
    PluginPermission.UiFileMenu | PluginPermission.UiApplication)]
public sealed class Plugin : DrivePlugin
{
    internal static readonly string[] Extensions = [".zip", ".7z", ".rar", ".tar"];
    private PluginWindowHost? _windows;
    private CancellationTokenSource? _enabled;
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(15), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>初始化窗口管理器并订阅本插件菜单、文件双击和账户变化。</summary>
    protected override void OnLoad()
    {
        _windows = new PluginWindowHost(Drive);
        Drive.Events.ActionInvoked += OnActionInvoked;
        Drive.Events.FileDoubleClicked += OnFileDoubleClicked;
        Drive.Events.UserChanged += OnUserChanged;
    }

    /// <summary>为当前启用周期创建取消令牌，并注册仅在支持的文件后缀上显示的菜单。</summary>
    protected override void OnEnable()
    {
        _enabled = new CancellationTokenSource();
        _ = RegisterMenuAsync(_enabled.Token);
    }

    /// <summary>注册压缩包文件菜单；异步失败写入主程序的插件日志。</summary>
    private async Task RegisterMenuAsync(CancellationToken token)
    {
        try
        {
            await Drive.UI.RegisterFileMenuAsync(new PluginAction
            {
                Id = "preview-archive", Title = "预览压缩包", Extensions = Extensions
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("注册压缩包菜单失败：" + error.Message); }
    }

    /// <summary>从插件卡片打开窗口，允许选择主程序当前目录里的压缩包。</summary>
    protected override void OnAppActivated()
    {
        _ = ShowAsync(null);
    }

    /// <summary>响应注册菜单，使用被点击文件的快照作为预览入口。</summary>
    private void OnActionInvoked(object? sender, DriveEvent data)
    {
        if (data.ActionId != "preview-archive") return;
        var file = data.Selection.FirstOrDefault(item => !item.IsFolder);
        if (file is not null) _ = ShowAsync(file);
    }

    /// <summary>双击支持的压缩包文件时打开或激活预览窗口；忽略文件夹及其他文件类型。</summary>
    /// <param name="sender">发送通知的插件事件集合。</param>
    /// <param name="data">宿主双击事件，Selection 包含被双击文件的快照。</param>
    /// <remarks>后缀匹配不区分大小写；仅响应通知，宿主原有的打开动作仍然执行。</remarks>
    private void OnFileDoubleClicked(object? sender, DriveEvent data)
    {
        var file = data.Selection.FirstOrDefault(item => !item.IsFolder &&
            Extensions.Contains(Path.GetExtension(item.Name), StringComparer.OrdinalIgnoreCase));
        if (file is null)
        {
            return;
        }

        _ = ShowAsync(file);
    }

    /// <summary>居中并激活窗口，随后异步加载文件；网络及解压工作不会阻塞插件 UI。</summary>
    private async Task ShowAsync(FileEntry? file)
    {
        if (_enabled is null || _enabled.IsCancellationRequested) return;
        try
        {
            ArchiveWindow? window = null;
            await _windows!.ShowNearHostAsync(() => window = new ArchiveWindow(Drive, _http, _enabled.Token),
                existing => window = (ArchiveWindow)existing, _enabled.Token);
            if (window is not null && file is not null) await window.OpenArchiveAsync(file);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Drive.Logger.Error("打开压缩包窗口失败：" + error.Message); }
    }

    /// <summary>账户切换时关闭窗口并取消正在读取的数据，防止继续展示上个账户的文件。</summary>
    private void OnUserChanged(object? sender, DriveEvent data)
    {
        _windows?.CloseWindow();
    }

    /// <summary>停用时立即撤销本轮异步任务并关闭窗口，菜单由宿主自动移除。</summary>
    protected override void OnDisable()
    {
        _enabled?.Cancel();
        _windows?.CloseWindow();
        _enabled?.Dispose(); _enabled = null;
    }

    /// <summary>退出时关闭窗口和 HTTP 连接，运行器负责结束进程。</summary>
    protected override void OnShutdown()
    {
        OnDisable(); _http.Dispose();
    }
}
