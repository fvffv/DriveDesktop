using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Drive.Plugin.Abi;

namespace Drive.Plugin.SDK.Protocol;

/// <summary>
/// 本地进程通信信封；只包含数据，绝不传递指针、委托或控件。
/// </summary>
internal sealed record PipeMessage
{
    public int Version { get; init; } = 2;
    public long Id { get; init; }
    public bool Reply { get; init; }
    public string Method { get; init; } = "";
    public PluginError Error { get; init; }
    public string Text { get; init; } = "";
    public long Number { get; init; }
    public long Generation { get; init; }
    public HostOperation Operation { get; init; }
    public DriveEventId EventId { get; init; }
    public HostRequest? Request { get; init; }
    public HostResponse? Response { get; init; }
    public DriveEvent? Event { get; init; }
    public PluginInfo? Info { get; init; }
}

/// <summary>
/// 为 NativeAOT 主程序生成完整的进程消息序列化代码。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PipeMessage))]
internal partial class PipeJsonContext : JsonSerializerContext;

/// <summary>
/// 有长度前缀、容量限制和双向请求关联的管道连接；读取响应独立于插件回调执行。
/// </summary>
internal sealed class PipeConnection : IDisposable
{
    private readonly Stream _stream;
    private readonly Func<PipeMessage, Task<PipeMessage>> _handler;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<PipeMessage>> _pending = new();
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextId;
    private int _incoming;
    private int _closed;
    internal Task Completion { get { return _completion.Task; } }

    /// <summary>
    /// 保存已连接的流和消息处理器，随后通过 Start 启动读写。
    /// </summary>
    /// <param name="stream">当前用户专用的双向管道。</param>
    /// <param name="handler">请求处理器；不得阻塞管道读取线程。</param>
    internal PipeConnection(Stream stream, Func<PipeMessage, Task<PipeMessage>> handler)
    {
        _stream = stream;
        _handler = handler;
    }

    /// <summary>
    /// 启动两个独立的异步读写循环；每个连接只能调用一次。
    /// </summary>
    internal void Start()
    {
        _ = ReadAsync();
        _ = WriteAsync();
    }

    /// <summary>
    /// 发送请求并等待响应；取消后通知对端取消同一请求，迟到响应自动丢弃。
    /// </summary>
    /// <param name="message">尚未分配 ID 的请求。</param>
    /// <param name="timeout">最大等待时间。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>经错误码检查的成功响应。</returns>
    internal async Task<PipeMessage> RequestAsync(PipeMessage message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        if (_pending.Count >= 128)
            throw new PluginException(PluginError.Busy, "插件通信请求过多。");
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            Send(message with { Id = id });
            var response = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (response.Error != PluginError.Success)
                throw new PluginException(response.Error, response.Text);
            return response;
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            TryNotify(new() { Method = "cancel", Number = id });
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// 尽力发送不需要响应的通知，断开或队列已满时返回 false。
    /// </summary>
    /// <param name="message">日志、取消等通知。</param>
    /// <returns>通知是否已进入有界发送队列。</returns>
    internal bool TryNotify(PipeMessage message)
    {
        try { Send(message with { Id = 0 }); return true; }
        catch (Exception error) when (error is IOException or ObjectDisposedException or PluginException) { return false; }
    }

    /// <summary>
    /// 序列化并限制单个消息大小，避免慢接收端无限占用内存。
    /// </summary>
    /// <param name="message">要发送的消息。</param>
    private void Send(PipeMessage message)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, PipeJsonContext.Default.PipeMessage);
        if (bytes.Length > AbiVersions.MaxMessageBytes)
            throw new PluginException(PluginError.TooLarge, "插件消息超过 8 MiB。");
        if (!_outgoing.Writer.TryWrite(bytes))
            throw new PluginException(PluginError.Busy, "插件通信发送队列已满。");
    }

    /// <summary>
    /// 唯一写入循环，确保并发请求不会交错破坏消息边界。
    /// </summary>
    /// <returns>连接关闭时完成的任务。</returns>
    private async Task WriteAsync()
    {
        try
        {
            await foreach (var bytes in _outgoing.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var prefix = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
                await _stream.WriteAsync(prefix, _stop.Token).ConfigureAwait(false);
                await _stream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
                await _stream.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { Close(error); }
    }

    /// <summary>
    /// 持续读取完整消息；响应直接完成等待任务，请求交给独立处理器。
    /// </summary>
    /// <returns>连接关闭时完成的任务。</returns>
    private async Task ReadAsync()
    {
        try
        {
            var prefix = new byte[4];
            while (!_stop.IsCancellationRequested)
            {
                await _stream.ReadExactlyAsync(prefix, _stop.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
                if (length <= 0 || length > AbiVersions.MaxMessageBytes)
                    throw new IOException("插件消息长度非法。");
                var bytes = new byte[length];
                await _stream.ReadExactlyAsync(bytes, _stop.Token).ConfigureAwait(false);
                var message = JsonSerializer.Deserialize(bytes, PipeJsonContext.Default.PipeMessage)
                    ?? throw new IOException("插件消息为空。");
                if (message.Version != 2 || message.Id < 0)
                    throw new IOException("插件通信协议不兼容。");
                if (message.Reply)
                {
                    if (_pending.TryRemove(message.Id, out var completion)) completion.TrySetResult(message);
                }
                else
                {
                    if (Interlocked.Increment(ref _incoming) > 128) throw new IOException("插件并发消息超过限制。");
                    _ = HandleAsync(message);
                }
            }
        }
        catch (Exception error) { Close(error); }
    }

    /// <summary>
    /// 执行请求并转换异常，避免插件错误终止管道读循环。
    /// </summary>
    /// <param name="message">对端请求。</param>
    /// <returns>响应发送完成的任务。</returns>
    private async Task HandleAsync(PipeMessage message)
    {
        try
        {
            PipeMessage result;
            try { result = await _handler(message).ConfigureAwait(false); }
            catch (Exception error)
            {
                result = new()
                {
                    Error = error switch
                    {
                        PluginException plugin => plugin.Error,
                        OperationCanceledException => PluginError.Cancelled,
                        ArgumentException or JsonException => PluginError.InvalidArgument,
                        _ => PluginError.Failed
                    },
                    Text = error.Message.Length > 4096 ? error.Message[..4096] : error.Message
                };
            }
            if (message.Id != 0)
            {
                try { Send(result with { Id = message.Id, Reply = true }); }
                catch (PluginException error) when (error.Error == PluginError.TooLarge)
                {
                    Send(new() { Id = message.Id, Reply = true, Error = error.Error, Text = error.Message });
                }
            }
        }
        catch (Exception error) { Close(error); }
        finally { Interlocked.Decrement(ref _incoming); }
    }

    /// <summary>
    /// 原子关闭连接，并让所有未完成请求立即失败。
    /// </summary>
    /// <param name="error">连接断开的原因。</param>
    private void Close(Exception error)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _stop.Cancel();
        _outgoing.Writer.TryComplete();
        _stream.Dispose();
        foreach (var pending in _pending.Values) pending.TrySetException(new IOException("插件进程连接已断开。", error));
        _pending.Clear();
        _completion.TrySetResult();
    }

    /// <summary>
    /// 释放管道并取消挂起通信；可以重复调用。
    /// </summary>
    public void Dispose()
    {
        Close(new ObjectDisposedException(nameof(PipeConnection)));
    }
}
