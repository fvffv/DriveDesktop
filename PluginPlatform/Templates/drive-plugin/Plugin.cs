using Avalonia.Controls;
using Drive.Plugin.Abi;
using Drive.Plugin.Avalonia;
using Drive.Plugin.SDK;

namespace DrivePluginApp;

/// <summary>
/// 普通托管插件入口；运行器提供 .NET、Avalonia 和 UI 主线程。
/// </summary>
/// <remarks>生命周期和应用入口在本文件；完整宿主事件处理方法见 Plugin.Events.cs。</remarks>
[DrivePlugin(Id = "com.example.DrivePluginApp", Name = "DrivePluginApp", Author = "插件作者", Version = "1.0.0",
    Description = "我的第一个网盘插件", IconBase64 = IconData.Ico, BackgroundColor = "#2B80F2", Tags = new[] { "工具", "示例" },
    Capabilities = PluginCapabilities.HasUi | PluginCapabilities.WindowsUi | PluginCapabilities.LinuxUi | PluginCapabilities.MacOsUi)]
// 按功能添加 FileRead、UploadRead、DownloadRead 等权限；Plugin.Events.cs 会订阅已声明权限对应的事件。
// 文件菜单需 UiFileMenu + FileRead，目录菜单需 UiFolderMenu + FileRead；还需在每次 OnEnable 时注册菜单。
[PluginPermission(PluginPermission.UiApplication | PluginPermission.UserRead)]
public sealed partial class Plugin : DrivePlugin
{
    private PluginWindowHost? _windows;
    private CancellationTokenSource? _enabled;

    /// <summary>
    /// 首次启用时初始化插件，可在这里订阅长期事件；Drive 已可使用。
    /// </summary>
    protected override void OnLoad()
    {
        _windows = new PluginWindowHost(Drive);
        SubscribeEvents();
        Drive.Logger.Info("插件已加载到独立运行器");
    }

    /// <summary>
    /// 每轮启用时启动本轮工作；耗时操作使用异步方法并捕获异常。
    /// </summary>
    protected override void OnEnable()
    {
        _enabled = new CancellationTokenSource();
        Drive.Logger.Info("插件已启用");
    }

    /// <summary>
    /// 用户点击插件卡片打开按钮，异步读取宿主位置后创建或激活插件窗口。
    /// </summary>
    protected override void OnAppActivated()
    {
        if (_enabled is null) return;
        _ = ShowWindowAsync(_enabled.Token);
    }

    /// <summary>
    /// 在宿主前方居中显示窗口，并处理停用期间的取消以及界面打开异常。
    /// </summary>
    /// <param name="cancellationToken">本轮启用的取消令牌，停用时撤销未完成的窗口打开请求。</param>
    /// <returns>窗口定位和显示完成的任务，不等待用户关闭窗口。</returns>
    private async Task ShowWindowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _windows!.ShowNearHostAsync(CreateWindow, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Drive.Logger.Info("Avalonia plugin window shown.");
        }
        catch (OperationCanceledException)
        {
            // 停用时正常取消，不作为插件错误记录。
        }
        catch (PluginException error) when (error.Error == PluginError.NotEnabled)
        {
            // 请求开始后插件可能已被停用。
        }
        catch (Exception error)
        {
            Drive.Logger.Error("打开插件窗口失败：" + error);
        }
    }

    /// <summary>由窗口管理器在插件 UI 线程创建 XAML 窗口；已有窗口会直接复用。</summary>
    /// <returns>绑定当前插件 SDK 上下文的新窗口。</returns>
    private Window CreateWindow()
    {
        return new MainWindow(Drive);
    }

    /// <summary>
    /// 停用时取消本地工作并关闭窗口；宿主业务 API 此时已撤销。
    /// </summary>
    protected override void OnDisable()
    {
        CancelAndCloseWindow();
    }

    /// <summary>
    /// 最终退出时清理本地资源；运行器负责结束消息循环和进程。
    /// </summary>
    protected override void OnShutdown()
    {
        CancelAndCloseWindow();
        UnsubscribeEvents();
    }

    /// <summary>取消本轮未完成的界面请求并关闭窗口；可重复调用，下次启用仍可重新打开。</summary>
    private void CancelAndCloseWindow()
    {
        _enabled?.Cancel();
        _enabled?.Dispose();
        _enabled = null;
        _windows?.CloseWindow();
    }
}
