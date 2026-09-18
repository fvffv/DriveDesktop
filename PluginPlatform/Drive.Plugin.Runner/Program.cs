using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Interop;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Runner;

/// <summary>
/// 自包含插件运行器入口；每个进程仅加载一个插件，UI 始终在进程主线程启动。
/// </summary>
internal static class Program
{
    internal static RunnerSession Session { get; private set; } = null!;

    /// <summary>
    /// 验证启动参数并连接主程序，在当前主线程运行插件应用。
    /// </summary>
    /// <param name="args">主程序提供的管道名和插件文件路径。</param>
    /// <returns>正常退出返回 0，初始化失败返回 1，参数错误返回 2。</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--plugin") return 2;
        try
        {
            var secret = Environment.GetEnvironmentVariable("DRIVE_PLUGIN_SESSION_TOKEN")
                ?? throw new InvalidOperationException("运行器必须由网盘主程序启动。");
            Environment.SetEnvironmentVariable("DRIVE_PLUGIN_SESSION_TOKEN", null);
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            Session = new(new PluginLoader(Path.GetFullPath(args[3])));
            using var connection = new PipeConnection(pipe, Session.HandleAsync);
            Session.Attach(connection);
            connection.Start();
            connection.RequestAsync(new() { Method = "hello", Text = secret, Info = Session.Info }, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            return Session.Run(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

}

/// <summary>
/// 串行调度一个插件的生命周期和事件，管道响应由独立读循环完成。
/// </summary>
internal sealed class RunnerSession
{
    private readonly PluginLoader _loader;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly ManualResetEventSlim _start = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PipeConnection _connection = null!;
    private DrivePlugin? _plugin;
    private PluginContext? _context;
    private RequestDispatcher? _requests;
    private bool _enabled;
    private long _generation;
    internal PluginInfo Info { get { return _loader.Info; } }
    private bool HasUi { get { return (Info.Capabilities & PluginCapabilities.HasUi) != 0; } }

    /// <summary>
    /// 保存尚未实例化插件的加载器。
    /// </summary>
    internal RunnerSession(PluginLoader loader)
    {
        _loader = loader;
    }

    /// <summary>
    /// 绑定连接并监视宿主退出，避免留下孤立的插件窗口和进程。
    /// </summary>
    internal void Attach(PipeConnection connection)
    {
        _connection = connection;
        _ = ExitWhenDisconnectedAsync();
    }

    /// <summary>
    /// 在进程主线程按需启动 Avalonia；无界面插件无需图形桌面。
    /// </summary>
    /// <returns>Avalonia 或后台运行器的退出码。</returns>
    internal int Run(string[] args)
    {
        if (!HasUi)
        {
            _ready.TrySetResult();
            _connection.Completion.GetAwaiter().GetResult();
            return 0;
        }
        _start.Wait();
        if (_connection.Completion.IsCompleted) return 0;
        try
        {
            return AppBuilder.Configure<RunnerApplication>().UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception error) { _ready.TrySetException(error); throw; }
    }

    /// <summary>
    /// 桌面初始化完成后释放等待的初始化命令。
    /// </summary>
    internal void MarkUiReady()
    {
        _ready.TrySetResult();
    }

    /// <summary>
    /// 主程序正常或异常断开时关闭当前运行器；失控 UI 最多再等待两秒。
    /// </summary>
    private async Task ExitWhenDisconnectedAsync()
    {
        await _connection.Completion.ConfigureAwait(false);
        _start.Set();
        if (HasUi && _ready.Task.IsCompletedSuccessfully)
        {
            Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
        }
        await Task.Delay(2000).ConfigureAwait(false);
        Environment.Exit(0);
    }

    /// <summary>
    /// 按序接收生命周期和事件命令，UI 插件的用户代码统一在 UI 线程执行。
    /// </summary>
    /// <param name="message">已连接主程序发来的命令。</param>
    /// <returns>生命周期调用结果。</returns>
    internal async Task<PipeMessage> HandleAsync(PipeMessage message)
    {
        if (message.Method == "cancel") return new();
        await Task.Yield();
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            if (message.Method == "initialize") _start.Set();
            await _ready.Task.ConfigureAwait(false);
            if (HasUi) await Dispatcher.UIThread.InvokeAsync(() => Execute(message));
            else Execute(message);
            if (HasUi && message.Method == "initialize")
            {
                var system = await _requests!.SendAsync(HostOperation.SystemInfo, new(), default).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() => ApplyTheme(system.System?.Theme ?? "light"));
            }
            return new();
        }
        catch (Exception error)
        {
            _connection.TryNotify(new() { Method = "log", Number = 2, Text = error.ToString() });
            throw;
        }
        finally { _commands.Release(); }
    }

    /// <summary>
    /// 执行插件回调；停用和关闭后由运行器兜底关闭全部窗口。
    /// </summary>
    /// <param name="message">当前命令和生命周期编号。</param>
    private void Execute(PipeMessage message)
    {
        switch (message.Method)
        {
            case "initialize":
                if (_plugin is not null) throw new InvalidOperationException("插件不能重复初始化。");
                _generation = message.Generation;
                _requests = new(_connection, _generation);
                _context = new(_requests, Info);
                _plugin = _loader.CreatePlugin();
                _plugin.Load(_context);
                break;
            case "enable":
                if (_plugin is null) throw new InvalidOperationException("插件尚未初始化。");
                _generation = message.Generation;
                _requests!.Resume(_generation);
                _context!.Events.Resume();
                _enabled = true;
                _plugin.Enable();
                break;
            case "disable":
                _enabled = false;
                _generation = message.Generation;
                _requests?.Suspend();
                _context?.Events.Suspend();
                try { _plugin?.Disable(); }
                finally { CloseWindows(); }
                break;
            case "shutdown":
                _enabled = false;
                _requests?.Shutdown();
                _context?.Events.Suspend();
                try { _plugin?.Shutdown(); }
                finally { CloseWindows(); }
                // 先返回响应，宿主随后断开连接，避免退出时丢失清理日志和响应。
                break;
            case "show":
                if (!_enabled || message.Generation != _generation) throw new PluginException(PluginError.NotEnabled, "插件已停用。");
                _plugin!.Activate();
                break;
            case "event":
                if (!_enabled || message.Generation != _generation) return;
                var data = message.Event ?? throw new ArgumentException("事件数据为空。");
                if (HasUi && data.Id == DriveEventId.ThemeChanged) ApplyTheme(data.Theme);
                _context!.Events.Dispatch(data);
                break;
            default:
                throw new PluginException(PluginError.Unsupported, "未知运行器命令。");
        }
    }

    /// <summary>
    /// 关闭运行器中所有插件窗口；拒绝关闭的窗口视为停用失败，由宿主终止该插件进程。
    /// </summary>
    private void CloseWindows()
    {
        if (!HasUi || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var window in desktop.Windows.ToArray()) window.Close();
        if (desktop.Windows.Count != 0) throw new InvalidOperationException("插件窗口拒绝关闭。");
    }

    /// <summary>
    /// 在插件进程 UI 线程同步宿主的明暗主题。
    /// </summary>
    /// <param name="theme">dark 为深色，其他值为浅色。</param>
    private static void ApplyTheme(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}

/// <summary>
/// 运行器共享的 Avalonia 应用，插件只创建 Window，无需再初始化 Application。
/// </summary>
internal sealed class RunnerApplication : Application
{
    /// <summary>
    /// 加载运行器统一提供的 Fluent 控件主题。
    /// </summary>
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    /// <summary>
    /// 框架和 Dispatcher 准备完毕后允许执行插件初始化命令。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Program.Session.MarkUiReady();
    }
}
