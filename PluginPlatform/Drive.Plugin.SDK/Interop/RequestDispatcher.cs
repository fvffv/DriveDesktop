using Drive.Plugin.Abi;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.SDK.Interop;

/// <summary>
/// 将类型化 SDK 调用转换为本地进程请求，并在停用时取消本轮请求。
/// </summary>
internal sealed class RequestDispatcher
{
    private readonly PipeConnection _connection;
    private readonly object _gate = new();
    private CancellationTokenSource _enabled = new();
    private bool _active = true;
    private long _generation;

    /// <summary>
    /// 绑定经过握手的运行器连接和本轮宿主生命周期编号。
    /// </summary>
    /// <param name="connection">运行器到主程序的管道。</param>
    /// <param name="generation">宿主分配的生命周期编号。</param>
    internal RequestDispatcher(PipeConnection connection, long generation)
    {
        _connection = connection;
        _generation = generation;
    }

    /// <summary>
    /// 发送宿主业务请求，保持调用方的 async/await、取消和错误码语义。
    /// </summary>
    /// <param name="operation">宿主操作编号。</param>
    /// <param name="request">不含宿主内部对象的请求数据。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>宿主成功响应。</returns>
    internal async Task<HostResponse> SendAsync(HostOperation operation, HostRequest request, CancellationToken cancellationToken)
    {
        CancellationToken lifetime;
        long generation;
        lock (_gate)
        {
            if (!_active) throw new PluginException(PluginError.NotEnabled, "插件已停用。");
            lifetime = _enabled.Token;
            generation = _generation;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        try
        {
            var reply = await _connection.RequestAsync(new()
            {
                Method = "invoke", Operation = operation, Request = request, Generation = generation
            }, TimeSpan.FromMinutes(2), linked.Token).ConfigureAwait(false);
            lifetime.ThrowIfCancellationRequested();
            return reply.Response ?? throw new PluginException(PluginError.Failed, "宿主响应为空。");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            throw new PluginException(PluginError.NotEnabled, "插件已停用，旧请求不再有效。");
        }
        catch (PluginException error) when (error.Error == PluginError.Cancelled)
        {
            // 跨进程的取消保留 async/await 语义，插件已有的取消分支可正常收尾。
            if (lifetime.IsCancellationRequested)
                throw new PluginException(PluginError.NotEnabled, "插件已停用，旧请求不再有效。");
            throw new OperationCanceledException($"{operation}：{error.Message}", error, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new PluginException(PluginError.Timeout, $"{operation}：宿主请求超时；已发出的服务端操作不保证撤销。");
        }
    }

    /// <summary>
    /// 首次绑定事件时向宿主检查权限并登记订阅；管道响应不依赖 UI 线程。
    /// </summary>
    /// <param name="id">要订阅的事件编号。</param>
    internal void Subscribe(DriveEventId id)
    {
        _connection.RequestAsync(new() { Method = "subscribe", EventId = id, Generation = _generation }, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 移除宿主订阅；管道断开时本地清理仍可继续。
    /// </summary>
    /// <param name="id">要退订的事件编号。</param>
    internal void Unsubscribe(DriveEventId id)
    {
        _connection.TryNotify(new() { Method = "unsubscribe", EventId = id, Generation = _generation });
    }

    /// <summary>
    /// 将日志加入通信队列，宿主补充可信的插件身份与时间。
    /// </summary>
    /// <param name="level">日志级别：0 信息、1 警告、2 错误、3 调试。</param>
    /// <param name="message">日志正文，最多保留 4096 个字符。</param>
    internal void Log(int level, string message)
    {
        message ??= "";
        _connection.TryNotify(new() { Method = "log", Number = level, Text = message.Length > 4096 ? message[..4096] : message });
    }

    /// <summary>
    /// 恢复下一轮业务请求，已取消的任务不会恢复。
    /// </summary>
    /// <param name="generation">宿主当前生命周期编号。</param>
    internal void Resume(long generation)
    {
        lock (_gate)
        {
            if (!_active) _enabled = new();
            _generation = generation;
            _active = true;
        }
    }

    /// <summary>
    /// 禁止新请求并取消本轮等待；不等待任何 UI 回调。
    /// </summary>
    internal void Suspend()
    {
        lock (_gate)
        {
            _active = false;
            _enabled.Cancel();
        }
    }

    /// <summary>
    /// 最终停止请求；连接本身由运行器负责关闭。
    /// </summary>
    internal void Shutdown()
    {
        Suspend();
    }
}
