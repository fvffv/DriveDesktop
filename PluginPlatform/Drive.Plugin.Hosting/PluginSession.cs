using System.Collections.Concurrent;
using System.Security.Cryptography;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.Hosting;

/// <summary>
/// 一个托管插件进程的权限、生命周期、事件与账户请求边界。
/// </summary>
internal sealed class PluginSession
{
    private readonly PluginHostOptions _options;
    private readonly IPluginHostAdapter _adapter;
    private readonly PluginLog _log;
    private readonly Action _changed;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _calls = new(1, 1);
    private readonly object _gate = new();
    private readonly HashSet<DriveEventId> _subscriptions = new();
    private readonly Dictionary<long, PendingRequest> _requests = new();
    private PluginProcess? _process;
    private PluginStorage? _storage;
    private PluginCallContext? _context;
    private bool _initialized;
    private bool _apiAllowed;
    private bool _shutdownComplete;
    private long _generation;
    private long _accountEpoch;
    private int _pendingEvents;
    private int _eventErrors;
    public PluginDescriptor Descriptor { get; }

    /// <summary>
    /// 创建尚未启动进程的插件会话。
    /// </summary>
    public PluginSession(PluginDescriptor descriptor, PluginHostOptions options, IPluginHostAdapter adapter, PluginLog log, Action changed)
    {
        Descriptor = descriptor; _options = options; _adapter = adapter; _log = log; _changed = changed;
    }

    /// <summary>
    /// 在独立运行器中读取插件元数据；主程序始终只接收数据。
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _process = new(_options, HandleAsync, Quarantine);
            Descriptor.Info = await _process.StartAsync(Descriptor.LibraryPath).ConfigureAwait(false);
            Descriptor.RunnerProcessId = _process.ProcessId;
            var directory = PluginStorage.NamespaceDirectory(_options.DataDirectory, Descriptor.Info.Id);
            _context = new(Descriptor.Info, directory);
            _storage = new(directory);
            State(PluginState.AwaitingApproval);
            Log("已读取托管插件信息，尚未实例化插件或初始化界面。");
        }
        catch (Exception error)
        {
            _process?.Dispose();
            State(error is PluginException { Error: PluginError.IncompatibleVersion } ? PluginState.Incompatible : PluginState.Faulted, error.Message);
            Log(error.Message, PluginLogLevel.Error);
        }
    }

    /// <summary>
    /// 用户授权后初始化并启用插件；新进程只在首次启用时创建插件入口。
    /// </summary>
    public async Task EnableAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Descriptor.State == PluginState.Enabled) return;
            if (_shutdownComplete || _process is null || Descriptor.State is PluginState.Faulted or PluginState.Incompatible)
                throw new PluginException(PluginError.Failed, "插件不可用，请修复后重启客户端。");
            await using (var file = File.OpenRead(Descriptor.LibraryPath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file).ConfigureAwait(false));
                if (hash != Descriptor.FileHash) throw new PluginException(PluginError.Failed, "插件文件已变更，请重启客户端后重新授权。");
            }
            State(PluginState.Initializing);
            lock (_gate) { _generation++; _apiAllowed = true; }
            if (!_initialized)
            {
                await CallAsync(new() { Method = "initialize", Generation = _generation }, _options.LoadTimeout).ConfigureAwait(false);
                _initialized = true;
            }
            await CallAsync(new() { Method = "enable", Generation = _generation }).ConfigureAwait(false);
            State(PluginState.Enabled);
            Log("已启用独立插件进程。");
            Publish(new() { Id = DriveEventId.ApplicationStarted });
        }
        catch (Exception error) { Quarantine(error.Message); throw; }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// 先撤销宿主能力，再在插件主线程停用并关闭全部窗口，保留进程供下次启用。
    /// </summary>
    public async Task DisableAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Revoke();
            if (Descriptor.State is PluginState.Faulted or PluginState.Incompatible) return;
            if (_initialized) await CallAsync(new() { Method = "disable", Generation = _generation }).ConfigureAwait(false);
            State(PluginState.Disabled);
            Log("已停用并关闭插件窗口。");
        }
        catch (Exception error) { Quarantine(error.Message); throw; }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// 依次发送退出事件、停用和最终清理，最后释放独立进程。
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_shutdownComplete) return;
        _shutdownComplete = true;
        try
        {
            if (Descriptor.State == PluginState.Enabled)
            {
                await SendEventAsync(new() { Id = DriveEventId.ApplicationStopping }, _generation).ConfigureAwait(false);
                await DisableAsync().ConfigureAwait(false);
            }
            if (_initialized && Descriptor.State is not (PluginState.Faulted or PluginState.Incompatible))
                await CallAsync(new() { Method = "shutdown", Generation = _generation }).ConfigureAwait(false);
        }
        finally { Revoke(); _process?.Dispose(); Descriptor.RunnerProcessId = null; }
    }

    /// <summary>
    /// 检查启用状态、应用权限和平台声明后，在运行器 UI 线程执行应用入口。
    /// </summary>
    public async Task ShowUiAsync()
    {
        if (Descriptor.State != PluginState.Enabled) throw new PluginException(PluginError.NotEnabled, "请先启用插件。");
        var platform = OperatingSystem.IsWindows() ? PluginCapabilities.WindowsUi : OperatingSystem.IsMacOS() ? PluginCapabilities.MacOsUi : PluginCapabilities.LinuxUi;
        var required = PluginCapabilities.HasUi | platform;
        if ((Descriptor.Info!.Capabilities & required) != required || !Has(PluginPermission.UiApplication))
            throw new PluginException(PluginError.Unsupported, "插件未声明当前平台的界面能力。");
        await CallAsync(new() { Method = "show", Generation = _generation }).ConfigureAwait(false);
    }

    /// <summary>
    /// 隔离失控或断开的插件并终止其进程；不终止宿主或其他插件。
    /// </summary>
    /// <param name="reason">隔离原因。</param>
    public void Quarantine(string reason)
    {
        Revoke();
        _process?.Dispose();
        Descriptor.RunnerProcessId = null;
        if (Descriptor.State != PluginState.Incompatible) State(PluginState.Faulted, reason);
        Log("插件进程已隔离：" + reason, PluginLogLevel.Error);
    }

    /// <summary>
    /// 撤销本轮所有业务请求、事件和菜单。
    /// </summary>
    private void Revoke()
    {
        lock (_gate)
        {
            _apiAllowed = false;
            _generation++;
            _subscriptions.Clear();
            foreach (var request in _requests.Values)
                request.Cancel(PluginError.NotEnabled, "插件已停用，旧请求不再有效。");
        }
        if (Descriptor.Info is not null)
        {
            try { _adapter.OnPluginDisabled(Descriptor.Info.Id); }
            catch (Exception error) { Log(error.Message, PluginLogLevel.Warning); }
        }
    }

    /// <summary>
    /// 账户变化时只取消读取或操作旧账户的请求；系统信息、菜单、插件存储和本地下载配置不受影响。
    /// </summary>
    public void CancelUserRequests()
    {
        lock (_gate)
        {
            _accountEpoch++;
            foreach (var request in _requests.Values)
                if (request.AccountBound) request.Cancel(PluginError.Cancelled, "账户已变化，旧账户请求已取消，请重新发起请求。");
        }
    }

    /// <summary>
    /// 判断声明权限是否覆盖操作要求。
    /// </summary>
    private bool Has(PluginPermission permission)
    {
        return (Descriptor.Info!.Permissions & permission) == permission;
    }

    /// <summary>
    /// 处理经过握手的插件消息，身份取自会话，绝不信任消息中的插件 ID。
    /// </summary>
    private Task<PipeMessage> HandleAsync(PipeMessage message)
    {
        switch (message.Method)
        {
            case "log":
                Log(message.Text, (PluginLogLevel)message.Number);
                break;
            case "cancel":
                lock (_gate)
                {
                    if (_requests.TryGetValue(message.Number, out var pending))
                        pending.Cancel(PluginError.Cancelled, "调用方已取消请求。");
                }
                break;
            case "unsubscribe":
                lock (_gate) { if (message.Generation == _generation) _subscriptions.Remove(message.EventId); }
                break;
            case "subscribe":
                lock (_gate)
                {
                    CheckAllowed(message.Generation, PluginPermissions.ForEvent(message.EventId));
                    if (!PluginPermissions.CanSubscribe(Descriptor.Info!.Permissions, message.EventId))
                        throw new PluginException(PluginError.PermissionDenied, "插件未获准订阅此事件。");
                    _subscriptions.Add(message.EventId);
                }
                break;
            case "invoke":
                return InvokeAsync(message);
            default:
                throw new PluginException(PluginError.Unsupported, "未知插件消息。");
        }
        return Task.FromResult(new PipeMessage());
    }

    /// <summary>
    /// 校验生命周期和权限；调用方持有会话锁。
    /// </summary>
    private void CheckAllowed(long generation, PluginPermission permission)
    {
        if (!_apiAllowed || generation != _generation) throw new PluginException(PluginError.NotEnabled, "插件未启用或请求已过期。");
        if (!Has(permission)) throw new PluginException(PluginError.PermissionDenied, "插件未获准使用此接口或事件。");
    }

    /// <summary>
    /// 在宿主执行真实业务或隔离存储，响应取消并过滤旧账户和旧生命周期结果。
    /// </summary>
    private async Task<PipeMessage> InvokeAsync(PipeMessage message)
    {
        PendingRequest pending;
        long accountEpoch;
        lock (_gate)
        {
            CheckAllowed(message.Generation, PluginPermissions.ForOperation(message.Operation));
            if (message.Operation is HostOperation.UiRegisterAction or HostOperation.UiUnregisterAction &&
                !PluginPermissions.HasMenuPermission(Descriptor.Info!.Permissions))
                throw new PluginException(PluginError.PermissionDenied, "插件未获准注册或移除菜单。");
            if (message.Id == 0 || message.Request is null) throw new ArgumentException("请求缺少 ID 或数据。");
            if (message.Operation == HostOperation.UiRegisterAction)
            {
                var action = message.Request.Action ?? throw new PluginException(PluginError.InvalidArgument, "请求缺少菜单定义。");
                var required = action.Location switch
                {
                    PluginActionLocation.FileMenu => PluginPermission.UiFileMenu | PluginPermission.FileRead,
                    PluginActionLocation.FolderMenu => PluginPermission.UiFolderMenu | PluginPermission.FileRead,
                    PluginActionLocation.Toolbar => PluginPermission.UiMenu,
                    _ => throw new PluginException(PluginError.InvalidArgument, "未知菜单位置。")
                };
                CheckAllowed(message.Generation, required);
            }
            if (_requests.Count >= 64) throw new PluginException(PluginError.Busy, "插件正在执行的请求过多。");
            pending = new(IsAccountBound(message.Operation));
            if (!_requests.TryAdd(message.Id, pending)) { pending.Dispose(); throw new ArgumentException("请求 ID 重复。"); }
            accountEpoch = _accountEpoch;
        }
        var token = pending.Cancellation.Token;
        try
        {
            // 离开管道读取循环后执行业务，取消、订阅及响应读取始终可继续。
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            var result = message.Operation is >= HostOperation.StorageGet and <= HostOperation.StorageEnumerate
                ? await _storage!.InvokeAsync(message.Operation, message.Request, token).ConfigureAwait(false)
                : await _adapter.InvokeAsync(_context!, message.Operation, message.Request, token).ConfigureAwait(false);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (!_apiAllowed || message.Generation != _generation || pending.AccountBound && accountEpoch != _accountEpoch)
                    throw new OperationCanceledException("账户或插件生命周期已变化。");
            }
            return new() { Response = result };
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException)
            {
                PluginException reason;
                lock (_gate)
                {
                    if (!_apiAllowed || message.Generation != _generation)
                        reason = new(PluginError.NotEnabled, "插件已停用，旧请求不再有效。");
                    else if (pending.AccountBound && accountEpoch != _accountEpoch)
                        reason = new(PluginError.Cancelled, "账户已变化，旧账户请求已取消，请重新发起请求。");
                    else if (pending.CancellationError != PluginError.Success)
                        reason = new(pending.CancellationError, pending.CancellationReason);
                    else if (token.IsCancellationRequested)
                        reason = new(PluginError.Timeout, "宿主请求超过两分钟执行期限；已发出的服务端操作不保证撤销。");
                    else
                        reason = new(PluginError.Cancelled, "宿主取消了本次操作：" + error.Message);
                }
                Log($"{message.Operation}: {reason.Message}", reason.Error == PluginError.Timeout ? PluginLogLevel.Warning : PluginLogLevel.Debug);
                throw reason;
            }
            Log($"{message.Operation}: {error.Message}", error is PluginException { Error: PluginError.Cancelled or PluginError.NotEnabled }
                ? PluginLogLevel.Debug : PluginLogLevel.Warning);
            throw;
        }
        finally
        {
            lock (_gate) { _requests.Remove(message.Id); pending.Dispose(); }
        }
    }

    /// <summary>仅豁免明确与账户无关的操作；新增接口默认接受账户隔离，防止旧数据泄漏。</summary>
    /// <param name="operation">经过权限校验的宿主操作。</param>
    /// <returns>是否需要在账户切换时取消，并过滤旧账户的返回值。</returns>
    private static bool IsAccountBound(HostOperation operation)
    {
        return operation is not (HostOperation.SystemInfo or HostOperation.DownloadSaveDirectory or
            HostOperation.UiRegisterAction or HostOperation.UiUnregisterAction or
            HostOperation.StorageGet or HostOperation.StorageSet or HostOperation.StorageDelete or HostOperation.StorageEnumerate);
    }

    /// <summary>记录一个宿主请求的取消范围与原因；所有修改均由会话锁串行保护。</summary>
    private sealed class PendingRequest(bool accountBound) : IDisposable
    {
        internal bool AccountBound { get; } = accountBound;
        internal CancellationTokenSource Cancellation { get; } = new(TimeSpan.FromMinutes(2));
        internal PluginError CancellationError { get; private set; }
        internal string CancellationReason { get; private set; } = "";

        /// <summary>保留首次显式取消原因；计时器先触发时仍按超时处理。</summary>
        internal void Cancel(PluginError error, string reason)
        {
            if (Cancellation.IsCancellationRequested) return;
            CancellationError = error;
            CancellationReason = reason;
            Cancellation.Cancel();
        }

        /// <summary>请求退出后释放执行期限计时器和取消注册。</summary>
        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }

    /// <summary>
    /// 将已订阅事件提交给插件，限制积压数量，避免慢插件拖住主程序。
    /// </summary>
    public void Publish(DriveEvent data)
    {
        long generation;
        lock (_gate)
        {
            if (Descriptor.State != PluginState.Enabled || !ShouldDeliver(data.Id)) return;
            generation = _generation;
        }
        if (Interlocked.Increment(ref _pendingEvents) > 64)
        {
            Interlocked.Decrement(ref _pendingEvents);
            Log("插件事件积压，已丢弃本次事件。", PluginLogLevel.Warning);
            return;
        }
        _ = PublishCoreAsync(data, generation);
    }

    /// <summary>
    /// 投递结束后释放事件积压计数。
    /// </summary>
    private async Task PublishCoreAsync(DriveEvent data, long generation)
    {
        try { await SendEventAsync(data, generation).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _pendingEvents); }
    }

    /// <summary>
    /// 发送当前生命周期仍有效的事件；连续失败时隔离插件。
    /// </summary>
    private async Task SendEventAsync(DriveEvent data, long generation)
    {
        try
        {
            lock (_gate)
            {
                if (!_apiAllowed || generation != _generation || !ShouldDeliver(data.Id)) return;
            }
            await CallAsync(new() { Method = "event", Event = data, Generation = generation }).ConfigureAwait(false);
            Interlocked.Exchange(ref _eventErrors, 0);
        }
        catch (Exception error)
        {
            Log("插件事件失败：" + error.Message, PluginLogLevel.Error);
            if (Interlocked.Increment(ref _eventErrors) >= 3) Quarantine("插件事件连续失败。");
        }
    }

    /// <summary>
    /// 按序调度生命周期与事件；等待和执行都有期限，超时只终止插件进程。
    /// </summary>
    private async Task CallAsync(PipeMessage message, TimeSpan? timeout = null)
    {
        var deadline = timeout ?? _options.CallTimeout;
        if (!await _calls.WaitAsync(deadline).ConfigureAwait(false))
        {
            Quarantine("插件调用排队超时。");
            throw new TimeoutException("插件调用排队超时。");
        }
        try { await _process!.CallAsync(message, deadline).ConfigureAwait(false); }
        catch (Exception error) when (error is TimeoutException or IOException)
        {
            Quarantine(error.Message);
            throw;
        }
        finally { _calls.Release(); }
    }

    /// <summary>
    /// 更新卡片状态并通知界面刷新。
    /// </summary>
    private void State(PluginState state, string error = "")
    {
        Descriptor.State = state; Descriptor.Error = error; _changed();
    }

    /// <summary>
    /// 判断事件是否被订阅；界面插件的主题同步由运行器自动维护。
    /// </summary>
    private bool ShouldDeliver(DriveEventId id)
    {
        return _subscriptions.Contains(id) || (id == DriveEventId.ThemeChanged &&
            (Descriptor.Info!.Capabilities & PluginCapabilities.HasUi) != 0);
    }

    /// <summary>
    /// 使用会话身份记录结构化日志。
    /// </summary>
    public void Log(string message, PluginLogLevel level = PluginLogLevel.Info)
    {
        _log.Write(Descriptor.DisplayName, message, level, Descriptor.Info?.Id ?? "");
    }
}
