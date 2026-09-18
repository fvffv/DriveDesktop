using Drive.Plugin.Abi;
using Drive.Plugin.SDK.Interop;
using Drive.Plugin.SDK.Protocol;

namespace Drive.Plugin.SDK;

/// <summary>
/// 读取和管理当前登录账户的网盘文件、目录。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class FilesApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal FilesApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 分页列出指定网盘目录下的文件和子目录。
    /// </summary>
    /// <param name="folderId">网盘目录 ID；null、空字符串或空白字符串使用当前账户的根目录，不是界面当前目录。</param>
    /// <param name="pageIndex">文件页码，从 1 开始。</param>
    /// <param name="pageSize">每页文件数量，范围为 1–200，默认为 50。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>包含文件分页结果和子目录列表的快照。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。目录与文件分别位于 FilePage.Folders 和 FilePage.Files；TotalFiles 为文件总数。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<FilePage> ListAsync(string? folderId = null, int pageIndex = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FileList, new() { FolderId = folderId ?? "", PageIndex = pageIndex, PageSize = pageSize }, cancellationToken)).Page!;
    }

    /// <summary>
    /// 获取一个网盘文件的详情。
    /// </summary>
    /// <param name="fileId">非空网盘文件 ID，不是本地路径或目录 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>文件详情快照。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<FileEntry> GetAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FileGet, new() { FileId = fileId }, cancellationToken)).File!;
    }

    /// <summary>
    /// 使用服务器搜索接口查找当前账户的文件。
    /// </summary>
    /// <param name="query">搜索关键词，最多 1000 个字符。</param>
    /// <param name="extensions">服务端文件类型筛选值；null 表示不限制。最多 32 项，每项最多 32 个字符，原样传给服务端 FileType，并非 SDK 在本地执行的扩展名过滤。</param>
    /// <param name="semantic">是否请求服务端语义搜索；能力与匹配规则取决于服务器。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>服务器返回的搜索结果；当前搜索接口不提供翻页参数。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。不要用返回的 PageSize 推断还有下一页；本方法不会自行截断搜索结果。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<FilePage> SearchAsync(string query, string[]? extensions = null, bool semantic = false, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FileSearch, new() { Query = query, Extensions = extensions ?? [], Semantic = semantic }, cancellationToken)).Page!;
    }

    /// <summary>
    /// 获取宿主文件页当前浏览的目录 ID。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>当前目录 ID；宿主没有当前目录时返回账户根目录 ID。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。获取当前目录后，可将其传给 ListAsync。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> GetCurrentFolderAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.CurrentFolder, new(), cancellationToken)).Text!;
    }

    /// <summary>
    /// 在网盘中创建一个子目录。
    /// </summary>
    /// <param name="folderId">父目录 ID；空字符串或空白字符串使用账户根目录。</param>
    /// <param name="name">新目录名，非空且最多 255 个字符；不能是 . 或 ..，不能包含路径分隔符、控制字符或文件名保留字符。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>服务器返回的新目录 ID。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileWrite"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> CreateFolderAsync(string folderId, string name, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FolderCreate, new() { FolderId = folderId, Name = name }, cancellationToken)).Text!;
    }

    /// <summary>
    /// 重命名网盘中的文件或目录。
    /// </summary>
    /// <param name="id">要重命名的文件或目录 ID。</param>
    /// <param name="name">新名称，非空且最多 255 个字符；不能是 . 或 ..，不能包含路径分隔符、控制字符或文件名保留字符。</param>
    /// <param name="isFolder">true 表示目录；false 表示文件。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>重命名请求完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileWrite"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task RenameAsync(string id, string name, bool isFolder = false, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.FileRename, new() { FileId = id, Name = name, IsFolder = isFolder }, cancellationToken);
    }

    /// <summary>
    /// 将一批文件和目录移动到指定网盘目录。
    /// </summary>
    /// <param name="destinationFolderId">目标目录 ID；空字符串或空白字符串使用账户根目录。</param>
    /// <param name="fileIds">文件 ID 数组；null 表示不移动文件。</param>
    /// <param name="folderIds">目录 ID 数组；null 表示不移动目录。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>移动请求完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileWrite"/> 权限。需要登录。文件与目录合计必须为 1–200 项；数组元素必须是有效的非空 ID。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task MoveAsync(string destinationFolderId, string[]? fileIds = null, string[]? folderIds = null, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.FileMove, new() { FolderId = destinationFolderId, FileIds = fileIds ?? [], FolderIds = folderIds ?? [] }, cancellationToken);
    }

    /// <summary>
    /// 删除指定的网盘文件和目录。
    /// </summary>
    /// <param name="fileIds">要删除的文件 ID 数组；null 表示不删除文件。</param>
    /// <param name="folderIds">要删除的目录 ID 数组；null 表示不删除目录。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>删除请求完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileDelete"/> 权限。需要登录。合计必须为 1–200 项。文件与目录分两次提交，不保证原子性：任务失败时，前一部分可能已经删除。SDK 不额外显示确认框。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task DeleteAsync(string[]? fileIds = null, string[]? folderIds = null, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.FileDelete, new() { FileIds = fileIds ?? [], FolderIds = folderIds ?? [] }, cancellationToken);
    }

    /// <summary>
    /// 将一个网盘文件复制到指定目录。
    /// </summary>
    /// <param name="fileId">源文件 ID；当前接口不支持复制目录。</param>
    /// <param name="destinationFolderId">目标目录 ID；空字符串或空白字符串使用账户根目录。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>复制请求完成的任务；不返回新文件 ID。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileWrite"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task CopyAsync(string fileId, string destinationFolderId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.FileCopy, new() { FileId = fileId, FolderId = destinationFolderId }, cancellationToken);
    }

    /// <summary>
    /// 为指定文件申请临时下载或播放地址。
    /// </summary>
    /// <param name="fileId">要下载或播放的网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>包含临时访问凭据的下载 URL。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。链接有效期由服务器决定，不应长期缓存或写入日志；过期后重新申请。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> GetDownloadUrlAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FileDownloadUrl, new() { FileId = fileId }, cancellationToken)).Text!;
    }

    /// <summary>
    /// 将一个小文件的完整内容读入内存。
    /// </summary>
    /// <param name="fileId">要读取的网盘文件 ID。</param>
    /// <param name="maxBytes">允许读取的最大字节数，范围为 1–4 MiB，默认 1 MiB；这不是读取前 N 字节的范围参数。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>文件的完整字节数组。</returns>
    /// <remarks>需要 <see cref="PluginPermission.FileRead"/> 权限。需要登录。文件大小或实际流长度超过上限时返回 TooLarge，不返回截断内容；大文件使用下载队列或临时下载地址。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<byte[]> ReadAsync(string fileId, int maxBytes = 1024 * 1024, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.FileRead, new() { FileId = fileId, MaxBytes = maxBytes }, cancellationToken)).Data;
    }
}

/// <summary>
/// 通过宿主上传队列添加、查询和控制当前账户的上传任务。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class UploadsApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal UploadsApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 将本地文件加入宿主的上传队列。
    /// </summary>
    /// <param name="localPath">已存在的本地文件路径，建议传绝对路径；相对路径按宿主工作目录解析。</param>
    /// <param name="folderId">网盘目标目录 ID；空字符串或空白字符串使用账户根目录。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主接受任务后返回的上传任务 ID，不是网盘文件 ID。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UploadCreate"/> 权限。需要登录。返回仅表示已加入队列，不代表上传完成；可通过上传事件或 ListAsync 获取后续状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> StartAsync(string localPath, string folderId, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.UploadStart, new() { LocalPath = localPath, FolderId = folderId }, cancellationToken)).Text!;
    }

    /// <summary>
    /// 读取当前账户的上传任务快照。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主当前上传列表中的任务，可能包含其他插件或用户创建的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UploadRead"/> 权限。需要登录。该列表不是仅限本插件的任务列表，也不是持续推送的进度流。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<TransferInfo[]> ListAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.UploadList, new(), cancellationToken)).Tasks;
    }

    /// <summary>
    /// 暂停当前账户的一个上传任务。
    /// </summary>
    /// <param name="taskId">宿主上传任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>暂停请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UploadControl"/> 权限。需要登录。任务必须仍在当前账户的上传列表中；对已处于目标暂停状态的任务不会重复切换；能否恢复取决于宿主任务状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task PauseAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UploadPause, new() { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// 继续当前账户的一个上传任务。
    /// </summary>
    /// <param name="taskId">宿主上传任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>继续请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UploadControl"/> 权限。需要登录。任务必须仍在当前账户的上传列表中；对已处于目标暂停状态的任务不会重复切换；能否恢复取决于宿主任务状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task ResumeAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UploadResume, new() { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// 取消当前账户的一个上传任务。
    /// </summary>
    /// <param name="taskId">宿主上传任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>取消请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UploadControl"/> 权限。需要登录。任务必须仍在当前账户的上传列表中；取消任务不等同于取消本次 API 等待，也不保证删除已产生的文件或服务端数据。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task CancelAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UploadCancel, new() { TaskId = taskId }, cancellationToken);
    }
}

/// <summary>
/// 读取宿主下载保存目录，并通过宿主下载队列添加、查询和控制当前账户的下载任务。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class DownloadsApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal DownloadsApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 获取主程序当前配置中的默认下载保存目录。
    /// </summary>
    /// <param name="cancellationToken">取消本次读取等待的令牌。</param>
    /// <returns>主程序已加载配置的 DownloadLocation 值；配置未设置该值时返回空字符串。</returns>
    /// <remarks>
    /// 需要 <see cref="PluginPermission.DownloadRead"/> 权限，不要求登录。
    /// 每次调用都读取主程序当前配置，设置页修改后再次调用即可获得新值；不会读取插件目录中的配置文件。
    /// 返回的是默认保存目录，不是某个已存在下载任务的保存路径；不创建目录，也不验证目录存在性或写入权限。
    /// </remarks>
    /// <exception cref="PluginException">插件未启用、权限不足或宿主读取失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> GetSaveDirectoryAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.DownloadSaveDirectory, new(), cancellationToken)).Text ?? "";
    }

    /// <summary>
    /// 将一个网盘文件加入宿主的下载队列。
    /// </summary>
    /// <param name="fileId">要下载的网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主接受任务后返回的下载任务 ID。</returns>
    /// <remarks>需要 <see cref="PluginPermission.DownloadCreate"/> 权限。需要登录。使用客户端配置的下载目录；返回不代表下载完成，可通过下载事件或 ListAsync 获取后续状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string> StartAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.DownloadStart, new() { FileId = fileId }, cancellationToken)).Text!;
    }

    /// <summary>
    /// 读取当前账户的下载任务快照。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主当前下载列表中的任务，可能包含其他插件或用户创建的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.DownloadRead"/> 权限。需要登录。该列表不是仅限本插件的任务列表，也不是持续推送的进度流。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<TransferInfo[]> ListAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.DownloadList, new(), cancellationToken)).Tasks;
    }

    /// <summary>
    /// 暂停当前账户的一个下载任务。
    /// </summary>
    /// <param name="taskId">宿主下载任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>暂停请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.DownloadControl"/> 权限。需要登录。任务必须仍在当前账户的下载列表中；对已处于目标暂停状态的任务不会重复切换；能否恢复取决于宿主任务状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task PauseAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.DownloadPause, new() { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// 继续当前账户的一个下载任务。
    /// </summary>
    /// <param name="taskId">宿主下载任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>继续请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.DownloadControl"/> 权限。需要登录。任务必须仍在当前账户的下载列表中；对已处于目标暂停状态的任务不会重复切换；能否恢复取决于宿主任务状态。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task ResumeAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.DownloadResume, new() { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// 取消当前账户的一个下载任务。
    /// </summary>
    /// <param name="taskId">宿主下载任务 ID，可从 StartAsync 或 ListAsync 获得，不是网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>取消请求处理完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.DownloadControl"/> 权限。需要登录。任务必须仍在当前账户的下载列表中；取消任务不等同于取消本次 API 等待，也不保证删除已产生的文件或服务端数据。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task CancelAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.DownloadCancel, new() { TaskId = taskId }, cancellationToken);
    }
}

/// <summary>
/// 读取当前账户的公开资料和存储空间摘要。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class UserApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal UserApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 获取当前登录状态和账户公开资料。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>用户资料；未登录时 IsLoggedIn 为 false。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UserRead"/> 权限。本方法允许在未登录时查询状态，不返回密码、访问令牌等凭据。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<UserInfo> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.UserCurrent, new(), cancellationToken)).User!;
    }

    /// <summary>
    /// 获取当前账户的已用空间和总配额。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>以字节为单位的存储容量摘要。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UserRead"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<StorageCapacity> GetStorageAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.UserStorage, new(), cancellationToken)).Capacity!;
    }
}

/// <summary>
/// 调用宿主界面功能，包括通知、确认框、菜单和页面导航。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class UiApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal UiApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 在宿主窗口显示一条插件通知。
    /// </summary>
    /// <param name="message">通知正文，最多 2000 个字符。</param>
    /// <param name="title">通知标题，最多 128 个字符；宿主会同时显示插件名称。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>通知已交给宿主显示后的任务，不等待用户阅读。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UiNotification"/> 权限。没有可显示通知的宿主窗口时失败。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task ShowNotificationAsync(string message, string title = "", CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UiNotify, new() { Text = message, Title = title }, cancellationToken);
    }

    /// <summary>
    /// 在宿主主窗口显示确认或取消对话框。
    /// </summary>
    /// <param name="message">需要用户确认的正文，最多 2000 个字符。</param>
    /// <param name="title">对话框标题，最多 128 个字符；宿主会同时显示插件名称。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>用户选择确认时为 true；取消或关闭对话框时为 false。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UiNotification"/> 权限。需要可用的网盘主窗口。等待用户选择也受请求超时限制；取消请求不保证已经显示的对话框立即关闭。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<bool> ConfirmAsync(string message, string title = "", CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.UiConfirm, new() { Text = message, Title = title }, cancellationToken)).Result;
    }

    /// <summary>
    /// 注册或更新当前插件的菜单项或工具栏操作。
    /// </summary>
    /// <param name="action">菜单定义；同一插件内 Id 相同的注册会覆盖旧定义。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>菜单注册完成的任务。</returns>
    /// <remarks>工具栏需要 UiMenu；文件菜单需要 UiFileMenu 和 FileRead；目录菜单需要 UiFolderMenu 和 FileRead，三种注册权限彼此独立。每个插件最多注册 32 项；点击通过 Drive.Events.ActionInvoked 通知。停用会清除菜单，再次启用时需重新注册。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task RegisterActionAsync(PluginAction action, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UiRegisterAction, new() { Action = action }, cancellationToken);
    }

    /// <summary>注册文件菜单，显示在文件条目的分享操作下面。</summary>
    /// <param name="action">菜单定义；Location 将自动设置为 FileMenu，Extensions 用于过滤文件后缀。</param>
    /// <param name="cancellationToken">取消本次等待的令牌；不能保证撤销已提交的注册。</param>
    /// <returns>宿主完成注册的任务。</returns>
    /// <remarks>需要 UiFileMenu 和 FileRead 权限；启用时注册，点击后通过 ActionInvoked 接收文件快照。</remarks>
    /// <exception cref="ArgumentNullException">action 为空。</exception>
    /// <exception cref="PluginException">权限不足、定义无效或插件已停用。</exception>
    /// <exception cref="OperationCanceledException">调用方取消等待。</exception>
    public Task RegisterFileMenuAsync(PluginAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RegisterActionAsync(action with { Location = PluginActionLocation.FileMenu }, cancellationToken);
    }

    /// <summary>注册文件夹菜单，显示在文件夹条目的重命名操作下面。</summary>
    /// <param name="action">菜单定义；Location 将自动设置为 FolderMenu，忽略 Extensions。</param>
    /// <param name="cancellationToken">取消本次等待的令牌；不能保证撤销已提交的注册。</param>
    /// <returns>宿主完成注册的任务。</returns>
    /// <remarks>需要 UiFolderMenu 和 FileRead 权限；此权限不会授予文件菜单的注册能力。</remarks>
    /// <exception cref="ArgumentNullException">action 为空。</exception>
    /// <exception cref="PluginException">权限不足、定义无效或插件已停用。</exception>
    /// <exception cref="OperationCanceledException">调用方取消等待。</exception>
    public Task RegisterFolderMenuAsync(PluginAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RegisterActionAsync(action with { Location = PluginActionLocation.FolderMenu }, cancellationToken);
    }

    /// <summary>
    /// 移除当前插件注册的一个菜单项或工具栏操作。
    /// </summary>
    /// <param name="actionId">注册时使用的菜单 ID；不存在时不做任何修改。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>菜单移除完成的任务。</returns>
    /// <remarks>需要 UiMenu、UiFileMenu、UiFolderMenu 中至少一项权限；只能按当前插件的命名空间移除自己注册的菜单。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task UnregisterActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UiUnregisterAction, new() { Key = actionId }, cancellationToken);
    }

    /// <summary>
    /// 让宿主切换到指定页面。
    /// </summary>
    /// <param name="page">宿主内置页面。</param>
    /// <param name="folderPath">仅 Files 页面使用的网盘界面路径，最多 1024 个字符；不是目录 ID 或本地磁盘路径。空字符串仅切换页面。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>导航消息已交给宿主的任务，不代表目标页面的数据已经加载。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UiApplication"/> 权限。需要登录。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task NavigateAsync(HostPage page, string folderPath = "", CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UiNavigate, new() { Page = page, Text = folderPath }, cancellationToken);
    }

    /// <summary>
    /// 使用宿主内置的预览或打开流程处理网盘文件。
    /// </summary>
    /// <param name="fileId">要打开的网盘文件 ID。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主打开流程返回后的任务，不表示用户已关闭预览窗口。</returns>
    /// <remarks>需要 <see cref="PluginPermission.UiApplication"/> 权限。需要登录，并同时具备 FileRead 权限；具体行为取决于文件类型和客户端支持的预览器。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task OpenFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.UiOpenFile, new() { FileId = fileId }, cancellationToken);
    }
}

/// <summary>
/// 访问以插件 ID 隔离、由宿主持久化的字符串键值数据。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class StorageApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal StorageApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 读取当前插件保存的字符串值。
    /// </summary>
    /// <param name="key">键名，UTF-8 编码后为 1–64 字节，区分大小写。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>保存的字符串；键不存在时为 null，已保存的空字符串仍返回空字符串。</returns>
    /// <remarks>需要 <see cref="PluginPermission.Storage"/> 权限。数据按插件 ID 隔离，不按网盘账户隔离；切换账户不会自动清空。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.StorageGet, new() { Key = key }, cancellationToken)).Text;
    }

    /// <summary>
    /// 创建或覆盖当前插件的一个字符串值。
    /// </summary>
    /// <param name="key">键名，UTF-8 编码后为 1–64 字节，区分大小写。</param>
    /// <param name="value">保存的字符串，UTF-8 编码后最多 64 KiB。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>值写入完成的任务。</returns>
    /// <remarks>需要 <see cref="PluginPermission.Storage"/> 权限。当前宿主限制每个插件最多 256 个键、总计 4 MiB；超额返回 TooLarge。单个值通过临时文件替换写入，不支持跨键事务。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.StorageSet, new() { Key = key, Value = value }, cancellationToken);
    }

    /// <summary>
    /// 删除当前插件的一个键及其值。
    /// </summary>
    /// <param name="key">键名，UTF-8 编码后为 1–64 字节。</param>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>删除完成的任务；键不存在时仍成功。</returns>
    /// <remarks>需要 <see cref="PluginPermission.Storage"/> 权限。只影响当前插件的数据。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return _requests.SendAsync(HostOperation.StorageDelete, new() { Key = key }, cancellationToken);
    }

    /// <summary>
    /// 列出当前插件保存的所有键名。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>按 Ordinal 顺序排列的键名快照；没有数据时为空数组。</returns>
    /// <remarks>需要 <see cref="PluginPermission.Storage"/> 权限。只返回键名，不返回值。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<string[]> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.StorageEnumerate, new(), cancellationToken)).Keys;
    }
}

/// <summary>
/// 查询宿主版本、平台、主题、语言和插件临时目录。
/// </summary>
/// <remarks>通过 PluginContext 获取实例。使用 await 等待，不要在生命周期或事件回调中使用 Wait/Result 阻塞插件的 UI 或生命周期回调。请求最长等待约两分钟；权限不足、未启用或宿主错误通过 PluginException 返回，调用方取消通过 OperationCanceledException 返回。</remarks>
public sealed class SystemApi
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal SystemApi(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 获取宿主运行环境和当前界面设置。
    /// </summary>
    /// <param name="cancellationToken">取消本次请求的令牌；取消或超时不能保证撤销已经提交给服务器的操作。</param>
    /// <returns>宿主信息快照，含版本、平台、语言、主题及当前插件的临时目录。</returns>
    /// <remarks>无需额外声明权限，也无需登录；插件仍须处于宿主允许调用 API 的生命周期阶段。</remarks>
    /// <exception cref = "PluginException">宿主拒绝调用或执行失败；具体原因见 <see cref="PluginException.Error"/>。</exception>
    /// <exception cref="OperationCanceledException">调用方取消了本次等待。</exception>
    public async Task<HostSystemInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        return (await _requests.SendAsync(HostOperation.SystemInfo, new(), cancellationToken)).System!;
    }
}

/// <summary>
/// 向宿主日志写入信息，由宿主自动补充时间、等级、插件名称和插件 ID。
/// </summary>
/// <remarks>可在初始化、启用及清理回调中使用；不要记录访问令牌或临时下载 URL。</remarks>
public sealed class PluginLogger
{
    private readonly RequestDispatcher _requests;
    /// <summary>
    /// 使用当前插件的请求调度器创建接口对象。
    /// </summary>
    /// <param name="requests">绑定当前插件生命周期和进程通信连接的调度器。</param>
    internal PluginLogger(RequestDispatcher requests)
    {
        _requests = requests;
    }

    /// <summary>
    /// 写入一条信息级别日志。
    /// </summary>
    /// <param name="message">日志内容；超过 4096 个 UTF-16 字符时截断并追加省略号，宿主会将换行折叠为空格。</param>
    /// <remarks>无需额外权限。方法同步提交日志，不返回磁盘写入结果。</remarks>
    public void Info(string message)
    {
        _requests.Log(0, message);
    }

    /// <summary>
    /// 写入一条警告级别日志。
    /// </summary>
    /// <param name="message">日志内容；最多保留 4096 个 UTF-16 字符，宿主会将换行折叠为空格。</param>
    /// <remarks>用于可恢复的问题或需要关注的状态。</remarks>
    public void Warning(string message)
    {
        _requests.Log(1, message);
    }

    /// <summary>
    /// 写入一条错误级别日志。
    /// </summary>
    /// <param name="message">日志内容；记录异常时建议传入 Exception.ToString()，以保留内部异常和调用堆栈。</param>
    /// <remarks>内容超过 4096 个 UTF-16 字符时截断；宿主会将换行折叠为空格。</remarks>
    public void Error(string message)
    {
        _requests.Log(2, message);
    }
}
