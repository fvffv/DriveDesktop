using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Drive.Plugin.SDK;

namespace Drive.Plugin.Avalonia;

/// <summary>
/// 在独立插件运行器的 Avalonia 应用中管理一个可重复打开的窗口。
/// </summary>
/// <remarks>
/// Windows、Linux、macOS 均由运行器在进程主线程初始化 Avalonia。
/// 本类不创建线程或 Application。插件也可以在 OnAppActivated 中直接 new Window 并 Show。
/// 运行器已将界面插件的生命周期与宿主事件调度到 UI 线程；后台任务仍需正常进行 UI 调度。
/// </remarks>
public sealed partial class PluginWindowHost
{
    private readonly PluginContext _context;
    private Window? _window;
    private bool _closed;
    private int _visibilityVersion;

    /// <summary>
    /// 创建窗口管理器，不初始化图形后端或启动线程。
    /// </summary>
    /// <param name="context">从 OnLoad 开始可用的插件上下文。</param>
    public PluginWindowHost(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// 在当前 UI 线程创建或激活窗口，可直接从 OnAppActivated 和菜单事件调用。
    /// </summary>
    /// <param name="createWindow">创建插件窗口的工厂，只在没有现存窗口时调用。</param>
    /// <param name="activateExisting">窗口已存在时可选的内容更新回调。</param>
    /// <exception cref="InvalidOperationException">未通过运行器初始化 Avalonia，或不在 UI 线程调用。</exception>
    /// <exception cref="ObjectDisposedException">本管理器已永久关闭。</exception>
    public void Show(Func<Window> createWindow, Action<Window>? activateExisting = null)
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        VerifyUiThread();
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_window is null)
        {
            _window = createWindow() ?? throw new InvalidOperationException("窗口工厂不能返回 null。");
            _window.Closed += OnWindowClosed;
        }
        else activateExisting?.Invoke(_window);
        _window.Show();
        _window.Activate();
        _context.Logger.Info("Avalonia plugin window shown.");
    }

    /// <summary>
    /// 从任意线程调度显示窗口；在 UI 线程调用时同步执行显示逻辑。
    /// </summary>
    /// <param name="createWindow">在 UI 线程执行的窗口工厂。</param>
    /// <param name="activateExisting">在 UI 线程执行的现存窗口更新回调。</param>
    /// <returns>本次显示调度完成的任务，不等待用户关闭窗口。</returns>
    public Task ShowAsync(Func<Window> createWindow, Action<Window>? activateExisting = null)
    {
        var version = Interlocked.Increment(ref _visibilityVersion);
        return InvokeAsync(() =>
        {
            if (version == Volatile.Read(ref _visibilityVersion)) Show(createWindow, activateExisting);
        });
    }

    /// <summary>
    /// 在 UI 线程关闭当前窗口，保留管理器供下次打开。
    /// </summary>
    public void CloseWindow()
    {
        VerifyUiThread();
        Interlocked.Increment(ref _visibilityVersion);
        _window?.Close();
    }

    /// <summary>
    /// 从任意线程关闭窗口；适合兼容已有插件的清理代码。
    /// </summary>
    /// <returns>窗口关闭调度完成的任务。</returns>
    public Task CloseWindowAsync()
    {
        Interlocked.Increment(ref _visibilityVersion);
        return InvokeAsync(CloseWindow);
    }

    /// <summary>
    /// 更新当前插件进程的主题；默认已由运行器同步宿主主题。
    /// </summary>
    /// <param name="theme">dark 表示深色，其他值按浅色处理。</param>
    /// <returns>主题设置完成的任务。</returns>
    public Task SetThemeAsync(string theme)
    {
        return InvokeAsync(() => Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    /// <summary>
    /// 永久关闭此管理器；运行器负责整个进程的最终退出。
    /// </summary>
    /// <returns>窗口关闭和本地清理完成的任务。</returns>
    /// <remarks>普通停用使用 CloseWindow 或 CloseWindowAsync；本方法调用后不能再次打开。</remarks>
    public Task ShutdownAsync()
    {
        Interlocked.Increment(ref _visibilityVersion);
        return InvokeAsync(() => { _closed = true; CloseWindow(); });
    }

    /// <summary>
    /// 用户关闭窗口后解除事件引用，允许下次创建新窗口。
    /// </summary>
    /// <param name="sender">刚关闭的窗口。</param>
    /// <param name="args">窗口关闭事件参数。</param>
    private void OnWindowClosed(object? sender, EventArgs args)
    {
        if (sender is Window window) window.Closed -= OnWindowClosed;
        if (ReferenceEquals(sender, _window)) _window = null;
        _context.Logger.Info("Avalonia plugin window closed.");
    }

    /// <summary>
    /// 检查运行器已初始化应用且调用位于它的 UI 线程。
    /// </summary>
    private static void VerifyUiThread()
    {
        if (Application.Current is null) throw new InvalidOperationException("请通过 Drive.Plugin.Runner 启动界面插件。");
        Dispatcher.UIThread.VerifyAccess();
    }

    /// <summary>
    /// 需要时切换到运行器 Dispatcher；当前已在 UI 线程时立即执行。
    /// </summary>
    /// <param name="action">待执行的 UI 操作。</param>
    /// <returns>操作完成的任务。</returns>
    private static async Task InvokeAsync(Action action)
    {
        if (Application.Current is null) throw new InvalidOperationException("请通过 Drive.Plugin.Runner 启动界面插件。");
        if (Dispatcher.UIThread.CheckAccess()) action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }
}
