using Drive.Plugin.Abi;
using Drive.Plugin.SDK.Interop;

namespace Drive.Plugin.SDK;

/// <summary>
/// 插件可订阅的宿主事件集合，通过 Drive.Events 获取。
/// </summary>
/// <remarks>
/// 界面插件的事件在运行器 UI 主线程触发；无界面插件使用串行后台调度。sender 为本 PluginEvents 实例。
/// 第一次添加处理器时同步向宿主订阅并检查权限；停用时暂停投递，重新启用时恢复保留的订阅。
/// 建议在 OnLoad 中订阅一次；反复使用 += 会重复注册处理器。
/// 事件不补发历史记录，也不是服务器全量变更流，只覆盖宿主观察到并发布的操作。
/// 处理器应尽快返回，不能同步等待宿主 API；界面插件可以直接更新控件，Task.Run 内仍须正常调度回 UI 线程。
/// 同步异常会中断本次剩余处理器并上报宿主；异步处理器必须自行捕获 await 后的异常。
/// </remarks>
public sealed class PluginEvents
{
    private readonly RequestDispatcher _requests;
    private readonly object _gate = new();
    private readonly Dictionary<DriveEventId, EventHandler<DriveEvent>?> _handlers = new();
    private bool _active = true;
    /// <summary>
    /// 创建绑定到当前请求调度器的事件集合。
    /// </summary>
    /// <param name="requests">负责宿主订阅与退订的调度器。</param>
    internal PluginEvents(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 添加本地处理器，并在首个处理器注册时建立宿主订阅。
    /// </summary>
    /// <param name="id">要监听的事件编号。</param>
    /// <param name="handler">处理器；null 时忽略。</param>
    private void Add(DriveEventId id, EventHandler<DriveEvent>? handler)
    {
        if (handler is null)
            return;
        lock (_gate)
        {
            _handlers.TryGetValue(id, out var current);
            if (current is null && _active)
                _requests.Subscribe(id);
            _handlers[id] = current + handler;
        }
    }

    /// <summary>
    /// 移除一次匹配的本地处理器，在最后一个处理器移除后退订宿主事件。
    /// </summary>
    /// <param name="id">事件编号。</param>
    /// <param name="handler">要移除的处理器。</param>
    private void Remove(DriveEventId id, EventHandler<DriveEvent>? handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(id, out var current))
                return;
            current -= handler;
            if (current is null)
            {
                _handlers.Remove(id);
                if (_active)
                    _requests.Unsubscribe(id);
            }
            else
                _handlers[id] = current;
        }
    }

    /// <summary>
    /// 取得处理器快照并在锁外同步分发一个事件；停用时忽略。
    /// </summary>
    /// <param name="data">已反序列化并核对事件编号的数据。</param>
    internal void Dispatch(DriveEvent data)
    {
        EventHandler<DriveEvent>? handler;
        lock (_gate)
        {
            if (!_active)
                return;
            _handlers.TryGetValue(data.Id, out handler);
        }

        handler?.Invoke(this, data);
    }

    /// <summary>
    /// 恢复保留的本地处理器对应的宿主订阅，不重复添加本地处理器。
    /// </summary>
    internal void Resume()
    {
        lock (_gate)
        {
            if (_active)
                return;
            _active = true;
            foreach (var id in _handlers.Keys)
                _requests.Subscribe(id);
        }
    }

    /// <summary>
    /// 暂停分发并退订宿主事件，保留处理器供下一轮启用恢复。
    /// </summary>
    internal void Suspend()
    {
        lock (_gate)
        {
            _active = false;
            foreach (var id in _handlers.Keys)
                _requests.Unsubscribe(id);
        }
    }

    /// <summary>
    /// 插件本轮启用成功后触发。
    /// </summary>
    /// <remarks>不需要额外权限。仅设置 Id；首次启用及停用后再次启用都会触发，不表示整个客户端只启动了一次。应在 OnLoad 或 OnEnable 返回前完成订阅。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ApplicationStarted
    {
        add
        {
            Add(DriveEventId.ApplicationStarted, value);
        }

        remove
        {
            Remove(DriveEventId.ApplicationStarted, value);
        }
    }

    /// <summary>
    /// 宿主正常关闭时，在停用当前已启用插件之前触发。
    /// </summary>
    /// <remarks>不需要额外权限。仅设置 Id；普通开关停用不会触发，强制退出、崩溃或已被隔离时不保证收到。清理资源仍应实现 OnDisable/OnShutdown。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ApplicationStopping
    {
        add
        {
            Add(DriveEventId.ApplicationStopping, value);
        }

        remove
        {
            Remove(DriveEventId.ApplicationStopping, value);
        }
    }

    /// <summary>
    /// 宿主账户资料或登录状态变更时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.UserRead"/> 权限。读取 User 了解新状态，User.IsLoggedIn 为 false 表示未登录。账户切换会取消旧账户请求；不保证订阅后立即补发当前状态。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> UserChanged
    {
        add
        {
            Add(DriveEventId.UserChanged, value);
        }

        remove
        {
            Remove(DriveEventId.UserChanged, value);
        }
    }

    /// <summary>
    /// 宿主文件页当前目录 ID 变更时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。FolderId 是新目录 ID，可能为空；这是导航状态通知，不表示目录内容加载完毕。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FolderChanged
    {
        add
        {
            Add(DriveEventId.FolderChanged, value);
        }

        remove
        {
            Remove(DriveEventId.FolderChanged, value);
        }
    }

    /// <summary>
    /// 宿主切换明暗主题时触发。
    /// </summary>
    /// <remarks>不需要额外权限。Theme 为 dark 或 light；插件须自行将主题更新调度到自己的 UI 线程。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ThemeChanged
    {
        add
        {
            Add(DriveEventId.ThemeChanged, value);
        }

        remove
        {
            Remove(DriveEventId.ThemeChanged, value);
        }
    }

    /// <summary>
    /// 宿主观察到创建目录、上传保存或合并文件成功时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。FolderId 通常为目标或父目录。创建目录时可提供 FolderIds 和 Name；上传完成的文件 ID 可能未知，FileIds 可为空。必要时重新查询目录。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileCreated
    {
        add
        {
            Add(DriveEventId.FileCreated, value);
        }

        remove
        {
            Remove(DriveEventId.FileCreated, value);
        }
    }

    /// <summary>
    /// 网盘文件或目录重命名成功后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。Name 为新名称，FileIds 或 FolderIds 标识目标；不提供旧名称，不保证 FolderId 已填充。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileRenamed
    {
        add
        {
            Add(DriveEventId.FileRenamed, value);
        }

        remove
        {
            Remove(DriveEventId.FileRenamed, value);
        }
    }

    /// <summary>
    /// 一批网盘文件或目录移动成功后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。FileIds、FolderIds 为本次请求中的目标，FolderId 为移动到的目录 ID。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FilesMoved
    {
        add
        {
            Add(DriveEventId.FilesMoved, value);
        }

        remove
        {
            Remove(DriveEventId.FilesMoved, value);
        }
    }

    /// <summary>
    /// 宿主确认一批文件或目录删除成功后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。读取 FileIds 和 FolderIds。文件页或 SDK 的一次混合批量删除会合并为一次通知，仅包含服务器确认成功的 ID；部分失败不代表整批回滚。独立删除请求分别通知，删除目录不会枚举其全部后代。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FilesDeleted
    {
        add
        {
            Add(DriveEventId.FilesDeleted, value);
        }

        remove
        {
            Remove(DriveEventId.FilesDeleted, value);
        }
    }

    /// <summary>
    /// 宿主观察到网盘文件复制成功后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。FileIds 当前携带源文件 ID，FolderId 为目标目录 ID，不是新副本 ID；需要新副本信息时重新查询目标目录。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileCopied
    {
        add
        {
            Add(DriveEventId.FileCopied, value);
        }

        remove
        {
            Remove(DriveEventId.FileCopied, value);
        }
    }

    /// <summary>
    /// 宿主开始执行文件打开或预览流程时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。FileIds 和 Selection 标识文件；不表示预览器初始化成功、文件播放完成或窗口关闭。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileOpened
    {
        add
        {
            Add(DriveEventId.FileOpened, value);
        }

        remove
        {
            Remove(DriveEventId.FileOpened, value);
        }
    }

    /// <summary>
    /// 宿主文件页选中的文件或目录变化时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.FileRead"/> 权限。Selection 是选中项快照，取消全部选择时为空；不要将该数组当作宿主可修改的选区集合。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> SelectionChanged
    {
        add
        {
            Add(DriveEventId.SelectionChanged, value);
        }

        remove
        {
            Remove(DriveEventId.SelectionChanged, value);
        }
    }

    /// <summary>
    /// 一个上传任务被宿主接受并加入队列时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.UploadRead"/> 权限。Transfer 为任务快照，状态通常为 Queued，FolderId 为上传目录；不表示已经开始发送数据。任务可能来自用户或其他插件。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> UploadStarted
    {
        add
        {
            Add(DriveEventId.UploadStarted, value);
        }

        remove
        {
            Remove(DriveEventId.UploadStarted, value);
        }
    }

    /// <summary>
    /// 一个上传任务成功完成后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.UploadRead"/> 权限。Transfer 为 Completed 状态，FolderId 为目标目录。上传任务的 Transfer.FileId 可能为空；文件详情应重新查询目录。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileUploaded
    {
        add
        {
            Add(DriveEventId.FileUploaded, value);
        }

        remove
        {
            Remove(DriveEventId.FileUploaded, value);
        }
    }

    /// <summary>
    /// 一个上传任务失败时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.UploadRead"/> 权限。Transfer 为 Failed 状态，Transfer.Error 在可获取时提供原因，也可能为空。FolderId 为目标目录。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> UploadFailed
    {
        add
        {
            Add(DriveEventId.UploadFailed, value);
        }

        remove
        {
            Remove(DriveEventId.UploadFailed, value);
        }
    }

    /// <summary>
    /// 一个上传任务被取消时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.UploadRead"/> 权限。Transfer 为 Cancelled 状态，FolderId 为目标目录。取消通知不承诺清理已经上传到服务器的临时数据。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> UploadCancelled
    {
        add
        {
            Add(DriveEventId.UploadCancelled, value);
        }

        remove
        {
            Remove(DriveEventId.UploadCancelled, value);
        }
    }

    /// <summary>
    /// 一个下载任务被宿主接受并加入队列时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.DownloadRead"/> 权限。Transfer 为任务快照，状态通常为 Queued；不表示已收到文件内容。任务可能来自用户或其他插件。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> DownloadStarted
    {
        add
        {
            Add(DriveEventId.DownloadStarted, value);
        }

        remove
        {
            Remove(DriveEventId.DownloadStarted, value);
        }
    }

    /// <summary>
    /// 一个下载任务成功完成后触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.DownloadRead"/> 权限。Transfer 为 Completed 状态，可通过 Transfer.FileId 关联网盘文件；当前数据契约不包含本地保存路径。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileDownloaded
    {
        add
        {
            Add(DriveEventId.FileDownloaded, value);
        }

        remove
        {
            Remove(DriveEventId.FileDownloaded, value);
        }
    }

    /// <summary>
    /// 一个下载任务失败时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.DownloadRead"/> 权限。Transfer 为 Failed 状态；Transfer.Error 仅在宿主提供原因时有值，插件应处理 null 或空字符串。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> DownloadFailed
    {
        add
        {
            Add(DriveEventId.DownloadFailed, value);
        }

        remove
        {
            Remove(DriveEventId.DownloadFailed, value);
        }
    }

    /// <summary>
    /// 一个下载任务被取消时触发。
    /// </summary>
    /// <remarks>需要 <see cref = "PluginPermission.DownloadRead"/> 权限。Transfer 为 Cancelled 状态；不保证本地临时文件已被删除。公共的线程、订阅和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> DownloadCancelled
    {
        add
        {
            Add(DriveEventId.DownloadCancelled, value);
        }

        remove
        {
            Remove(DriveEventId.DownloadCancelled, value);
        }
    }

    /// <summary>网盘目录创建成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。FolderId 是父目录 ID，FolderIds 是服务器返回的新目录 ID（未返回时为空），Name 是目录名。兼容已有插件时也会发送 FileCreated；同一业务请只订阅其中一个。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FolderCreated
    {
        add
        {
            Add(DriveEventId.FolderCreated, value);
        }
        remove
        {
            Remove(DriveEventId.FolderCreated, value);
        }
    }

    /// <summary>用户双击文件条目时触发，仅作通知。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。Selection 包含双击文件的快照，FileIds 包含文件 ID。宿主继续执行原来的打开或预览，不等待插件处理；本事件没有取消或捕获返回值。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileDoubleClicked
    {
        add
        {
            Add(DriveEventId.FileDoubleClicked, value);
        }
        remove
        {
            Remove(DriveEventId.FileDoubleClicked, value);
        }
    }

    /// <summary>一次文件搜索成功返回后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。Search 包含普通/语义搜索的条件、数量和最多 200 条结果；用 ResultsTruncated 判断快照是否截断。失败或抛异常的搜索不触发。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> FileSearched
    {
        add
        {
            Add(DriveEventId.FileSearched, value);
        }
        remove
        {
            Remove(DriveEventId.FileSearched, value);
        }
    }

    /// <summary>文件分享链接创建成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。ShareLink 包含 FileId、分享密钥、有效期和简介；创建接口不返回分享记录 ID，因此 ShareId 可能为空。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ShareLinkCreated
    {
        add
        {
            Add(DriveEventId.ShareLinkCreated, value);
        }
        remove
        {
            Remove(DriveEventId.ShareLinkCreated, value);
        }
    }

    /// <summary>文件分享链接设置更新成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。ShareLink 包含 ShareId、已知 FileId 和更新后的设置；不包含密码明文。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ShareLinkUpdated
    {
        add
        {
            Add(DriveEventId.ShareLinkUpdated, value);
        }
        remove
        {
            Remove(DriveEventId.ShareLinkUpdated, value);
        }
    }

    /// <summary>文件分享链接删除成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。ShareLink 包含 ShareId 和删除前已知的设置；FileIds 只在宿主已读取相关分享记录时提供。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ShareLinkDeleted
    {
        add
        {
            Add(DriveEventId.ShareLinkDeleted, value);
        }
        remove
        {
            Remove(DriveEventId.ShareLinkDeleted, value);
        }
    }

    /// <summary>当前账户的自定义视图添加并保存成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.UserRead"/> 权限。View 是新视图快照；批量添加默认视图时每个新增视图分别通知。打开编辑框或载入已有视图不触发。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ViewAdded
    {
        add
        {
            Add(DriveEventId.ViewAdded, value);
        }
        remove
        {
            Remove(DriveEventId.ViewAdded, value);
        }
    }

    /// <summary>当前账户的自定义视图删除并保存成功后触发。</summary>
    /// <remarks>需要 <see cref="PluginPermission.UserRead"/> 权限。View 是删除前的视图快照。保存失败不触发。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足或插件不可用而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ViewDeleted
    {
        add
        {
            Add(DriveEventId.ViewDeleted, value);
        }
        remove
        {
            Remove(DriveEventId.ViewDeleted, value);
        }
    }

    /// <summary>
    /// 用户点击当前插件注册的菜单或工具栏操作时触发。
    /// </summary>
    /// <remarks>需要 UiMenu、UiFileMenu、UiFolderMenu 中至少一项权限。只接收当前插件自己注册的操作；ActionId 对应 PluginAction.Id。条目菜单的 Selection 是被点击条目的快照，工具栏使用当前选中项，没有 FileRead 时为空。应用卡片打开按钮走 OnAppActivated。线程和异常规则见 <see cref="PluginEvents"/>。</remarks>
    /// <exception cref="PluginException">首次订阅时，宿主因权限不足、插件不可用或其他错误而拒绝订阅。</exception>
    public event EventHandler<DriveEvent> ActionInvoked
    {
        add
        {
            Add(DriveEventId.ActionInvoked, value);
        }

        remove
        {
            Remove(DriveEventId.ActionInvoked, value);
        }
    }
}
