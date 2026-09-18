using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Hosting;

/// <summary>
/// 只管理本会话启动的运行器进程；主程序不加载任何插件程序集。
/// </summary>
internal sealed class PluginProcess : IDisposable
{
    private readonly PluginHostOptions _options;
    private readonly Func<PipeMessage, Task<PipeMessage>> _handler;
    private readonly Action<string> _disconnected;
    private readonly TaskCompletionSource<PluginInfo> _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object _outputGate = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private PipeConnection? _connection;
    private string _output = "";
    private int _disposed;
    private bool _authenticated;
    internal int? ProcessId { get { return _process?.Id; } }

    /// <summary>
    /// 保存运行器位置、宿主处理器与异常退出回调。
    /// </summary>
    /// <param name="options">宿主运行配置。</param>
    /// <param name="handler">经过身份绑定后的宿主请求处理器。</param>
    /// <param name="disconnected">连接异常终止时撤销插件权限的回调。</param>
    internal PluginProcess(PluginHostOptions options, Func<PipeMessage, Task<PipeMessage>> handler, Action<string> disconnected)
    {
        _options = options;
        _handler = handler;
        _disconnected = disconnected;
    }

    /// <summary>
    /// 启动独立进程并验证协议、随机凭据及插件元数据；尚不创建插件实例或窗口。
    /// </summary>
    /// <param name="pluginPath">普通托管插件 DLL 的绝对路径。</param>
    /// <returns>经过校验的插件信息。</returns>
    internal async Task<PluginInfo> StartAsync(string pluginPath)
    {
        if (!File.Exists(_options.RunnerPath))
            throw new FileNotFoundException("缺少插件运行器，请将运行器启动文件放在客户端主程序同一目录。", _options.RunnerPath);
        var pipeName = "drive-plugin-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(_options.RunnerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // 运行器本身没有窗口；保留正常显示方式，避免 Windows 隐藏插件首次 Show 的窗口。
            WindowStyle = ProcessWindowStyle.Normal,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_options.RunnerPath)!
        };
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--plugin"); start.ArgumentList.Add(Path.GetFullPath(pluginPath));
        start.Environment["DRIVE_PLUGIN_SESSION_TOKEN"] = _secret;
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += CaptureOutput;
        _process.ErrorDataReceived += CaptureOutput;
        try
        {
            _process.Start();
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            using var deadline = new CancellationTokenSource(_options.LoadTimeout);
            var connected = _pipe.WaitForConnectionAsync(deadline.Token);
            var exited = _process.WaitForExitAsync(deadline.Token);
            if (await Task.WhenAny(connected, exited).ConfigureAwait(false) == exited)
                throw new InvalidOperationException("插件运行器在连接前退出：" + GetOutput());
            await connected.ConfigureAwait(false);
            _connection = new(_pipe, HandleAsync);
            _connection.Start();
            _ = WatchAsync();
            return await _hello.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>
    /// 首条请求必须携带正确凭据；之后所有消息都绑定到本插件会话。
    /// </summary>
    /// <param name="message">运行器发送的消息。</param>
    /// <returns>宿主响应。</returns>
    private async Task<PipeMessage> HandleAsync(PipeMessage message)
    {
        if (!_authenticated)
        {
            try
            {
                if (message.Method != "hello" || message.Text != _secret || message.Info is null)
                    throw new IOException("插件运行器握手失败。");
                PluginMetadata.Validate(message.Info);
                _authenticated = true;
                _hello.TrySetResult(message.Info);
                return new();
            }
            catch (Exception error) { _hello.TrySetException(error); throw; }
        }
        return await _handler(message).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送生命周期或事件调用，超时由会话负责隔离并关闭进程。
    /// </summary>
    /// <param name="message">插件控制请求。</param>
    /// <param name="timeout">调用期限。</param>
    /// <returns>插件响应。</returns>
    internal Task<PipeMessage> CallAsync(PipeMessage message, TimeSpan timeout)
    {
        // 用户在宿主点击打开或插件菜单时，允许这个运行器把自己的窗口切到前台。
        if (OperatingSystem.IsWindows() && _process is not null &&
            (message.Method == "show" || message.Method == "event" && message.Event?.Id == Drive.Plugin.Abi.DriveEventId.ActionInvoked))
        {
            AllowSetForegroundWindow((uint)_process.Id);
        }
        return (_connection ?? throw new IOException("插件尚未连接。")).RequestAsync(message, timeout);
    }

    /// <summary>将当前用户操作产生的前台激活资格授予指定插件进程；失败时由窗口系统正常处理激活。</summary>
    /// <param name="processId">当前会话启动的运行器进程 ID。</param>
    /// <returns>是否成功授予前台激活资格。</returns>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    /// <summary>
    /// 持续观察连接，宿主消失或插件崩溃时及时清理本会话。
    /// </summary>
    /// <returns>连接终止后完成的任务。</returns>
    private async Task WatchAsync()
    {
        await _connection!.Completion.ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) == 0)
        {
            var reason = "插件运行器已断开：" + GetOutput();
            _hello.TrySetException(new IOException(reason));
            _disconnected(reason);
        }
    }

    /// <summary>
    /// 收集有限长度的运行器输出，避免第三方输出撑满内存或阻塞子进程。
    /// </summary>
    /// <param name="sender">进程对象。</param>
    /// <param name="args">本行输出。</param>
    private void CaptureOutput(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null) return;
        lock (_outputGate)
        {
            _output += args.Data + Environment.NewLine;
            if (_output.Length > 4096) _output = _output[^4096..];
        }
    }

    /// <summary>
    /// 读取诊断输出快照。
    /// </summary>
    /// <returns>最多 4096 字符的最近输出。</returns>
    private string GetOutput()
    {
        lock (_outputGate) return _output;
    }

    /// <summary>
    /// 关闭管道并结束本对象实际启动的进程，不影响其他插件或用户进程。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connection?.Dispose();
        _pipe?.Dispose();
        if (_process is not null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
        }
    }
}
