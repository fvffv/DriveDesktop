using Avalonia.Controls;
using Avalonia.Interactivity;
using Drive.Plugin.SDK;

namespace DrivePluginApp;

/// <summary>
/// 插件自己的 Avalonia 窗口，通过 SDK 显示当前账户信息。
/// </summary>
public partial class MainWindow : Window
{
    private readonly PluginContext _drive;
    private readonly CancellationTokenSource _closed = new();
    /// <summary>
    /// 创建插件窗口并绑定当前插件的宿主上下文。
    /// </summary>
    /// <param name="drive">当前运行器中属于此插件的 Drive 上下文。</param>
    public MainWindow(PluginContext drive)
    {
        InitializeComponent();
        _drive = drive;
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 窗口关闭时取消仍在等待的用户查询，并释放本窗口的取消令牌源。
    /// </summary>
    /// <param name="sender">触发关闭的插件窗口。</param>
    /// <param name="args">窗口关闭事件参数。</param>
    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _closed.Cancel();
        _closed.Dispose();
    }

    /// <summary>
    /// 处理读取用户按钮点击，异步查询账户资料并更新窗口。
    /// </summary>
    /// <param name="sender">触发点击的控件。</param>
    /// <param name="args">Avalonia 路由事件参数。</param>
    /// <remarks>这是 UI 事件处理器，运行在插件 UI 线程。窗口关闭时取消等待；所有异步异常在方法内部处理。</remarks>
    private async void ReadUserClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            var user = await _drive.User.GetCurrentAsync(_closed.Token);
            StatusText.Text = user.IsLoggedIn ? $"欢迎，{user.Nickname}" : "请先在客户端登录。";
            _drive.Logger.Info("已读取用户信息");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
            _drive.Logger.Error(error.Message);
        }
    }
}
