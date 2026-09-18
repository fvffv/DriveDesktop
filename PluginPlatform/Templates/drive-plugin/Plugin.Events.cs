using Drive.Plugin.Abi;
using Drive.Plugin.SDK;

namespace DrivePluginApp;

// 本文件与 Plugin.cs 组成同一个插件类；所有宿主事件均提供具名处理方法。
public sealed partial class Plugin
{
    /// <summary>首次加载时按插件声明权限订阅宿主事件，只在 OnLoad 调用一次。</summary>
    /// <remarks>
    /// 需要更多事件时，先在 Plugin.cs 的 PluginPermission 特性中添加对应权限，再重新编译并在宿主授权。
    /// 停用时 SDK 自动暂停投递，重新启用时恢复；不要在 OnEnable 重复添加这些处理器。
    /// 界面插件处理器在 Runner UI 线程执行，不要使用 Wait/Result 同步等待宿主 API。
    /// 若改为异步事件处理器，必须自行捕获异常并使用本轮启用的取消令牌。
    /// </remarks>
    private void SubscribeEvents()
    {
        var permissions = Info.Permissions;

        // 应用和主题事件（不需要额外权限）
        Drive.Events.ApplicationStarted += OnApplicationStarted;
        Drive.Events.ApplicationStopping += OnApplicationStopping;
        Drive.Events.ThemeChanged += OnThemeChanged;

        // 账户和视图事件
        if ((permissions & PluginPermission.UserRead) != 0)
        {
            Drive.Events.UserChanged += OnUserChanged;
            Drive.Events.ViewAdded += OnViewAdded;
            Drive.Events.ViewDeleted += OnViewDeleted;
        }

        // 网盘目录、文件、搜索和分享事件
        if ((permissions & PluginPermission.FileRead) != 0)
        {
            Drive.Events.FolderChanged += OnFolderChanged;
            Drive.Events.FileCreated += OnFileCreated;
            Drive.Events.FolderCreated += OnFolderCreated;
            Drive.Events.FileRenamed += OnFileRenamed;
            Drive.Events.FilesMoved += OnFilesMoved;
            Drive.Events.FilesDeleted += OnFilesDeleted;
            Drive.Events.FileCopied += OnFileCopied;
            Drive.Events.FileOpened += OnFileOpened;
            Drive.Events.FileDoubleClicked += OnFileDoubleClicked;
            Drive.Events.SelectionChanged += OnSelectionChanged;
            Drive.Events.FileSearched += OnFileSearched;
            Drive.Events.ShareLinkCreated += OnShareLinkCreated;
            Drive.Events.ShareLinkUpdated += OnShareLinkUpdated;
            Drive.Events.ShareLinkDeleted += OnShareLinkDeleted;
        }

        // 上传事件
        if ((permissions & PluginPermission.UploadRead) != 0)
        {
            Drive.Events.UploadStarted += OnUploadStarted;
            Drive.Events.FileUploaded += OnFileUploaded;
            Drive.Events.UploadFailed += OnUploadFailed;
            Drive.Events.UploadCancelled += OnUploadCancelled;
        }

        // 下载事件
        if ((permissions & PluginPermission.DownloadRead) != 0)
        {
            Drive.Events.DownloadStarted += OnDownloadStarted;
            Drive.Events.FileDownloaded += OnFileDownloaded;
            Drive.Events.DownloadFailed += OnDownloadFailed;
            Drive.Events.DownloadCancelled += OnDownloadCancelled;
        }

        // 菜单点击事件
        if ((permissions & (PluginPermission.UiMenu | PluginPermission.UiFileMenu | PluginPermission.UiFolderMenu)) != 0)
        {
            Drive.Events.ActionInvoked += OnActionInvoked;
        }
    }

    /// <summary>最终退出时解除全部本地处理器；未订阅的事件可安全移除。</summary>
    /// <remarks>在 OnShutdown 调用，此时 SDK 已暂停事件投递；普通停用保留订阅供下次启用恢复。</remarks>
    private void UnsubscribeEvents()
    {
        Drive.Events.ApplicationStarted -= OnApplicationStarted;
        Drive.Events.ApplicationStopping -= OnApplicationStopping;
        Drive.Events.ThemeChanged -= OnThemeChanged;
        Drive.Events.UserChanged -= OnUserChanged;
        Drive.Events.ViewAdded -= OnViewAdded;
        Drive.Events.ViewDeleted -= OnViewDeleted;
        Drive.Events.FolderChanged -= OnFolderChanged;
        Drive.Events.FileCreated -= OnFileCreated;
        Drive.Events.FolderCreated -= OnFolderCreated;
        Drive.Events.FileRenamed -= OnFileRenamed;
        Drive.Events.FilesMoved -= OnFilesMoved;
        Drive.Events.FilesDeleted -= OnFilesDeleted;
        Drive.Events.FileCopied -= OnFileCopied;
        Drive.Events.FileOpened -= OnFileOpened;
        Drive.Events.FileDoubleClicked -= OnFileDoubleClicked;
        Drive.Events.SelectionChanged -= OnSelectionChanged;
        Drive.Events.FileSearched -= OnFileSearched;
        Drive.Events.ShareLinkCreated -= OnShareLinkCreated;
        Drive.Events.ShareLinkUpdated -= OnShareLinkUpdated;
        Drive.Events.ShareLinkDeleted -= OnShareLinkDeleted;
        Drive.Events.UploadStarted -= OnUploadStarted;
        Drive.Events.FileUploaded -= OnFileUploaded;
        Drive.Events.UploadFailed -= OnUploadFailed;
        Drive.Events.UploadCancelled -= OnUploadCancelled;
        Drive.Events.DownloadStarted -= OnDownloadStarted;
        Drive.Events.FileDownloaded -= OnFileDownloaded;
        Drive.Events.DownloadFailed -= OnDownloadFailed;
        Drive.Events.DownloadCancelled -= OnDownloadCancelled;
        Drive.Events.ActionInvoked -= OnActionInvoked;
    }

    #region 应用和主题事件（不需要额外权限）

    /// <summary>本轮插件启用成功后触发；每次重新启用都会再次收到。</summary>
    /// <remarks>订阅权限：无需额外权限。可在此刷新启用后的状态；不同于只调用一次的 OnLoad。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnApplicationStarted(object? sender, DriveEvent e)
    {
        // 可在此刷新启用后的状态；不同于只调用一次的 OnLoad。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>宿主正常退出、停用插件之前触发；普通停用不会触发。</summary>
    /// <remarks>订阅权限：无需额外权限。可在此处理退出通知；强制退出不保证收到，资源清理仍放在 OnDisable/OnShutdown。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnApplicationStopping(object? sender, DriveEvent e)
    {
        // 可在此处理退出通知；强制退出不保证收到，资源清理仍放在 OnDisable/OnShutdown。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>宿主切换明暗主题后触发，e.Theme 为 dark 或 light。</summary>
    /// <remarks>订阅权限：无需额外权限。Runner 已同步 Avalonia 主题；在此刷新自己绘制的颜色等自定义状态。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnThemeChanged(object? sender, DriveEvent e)
    {
        // Runner 已同步 Avalonia 主题；在此刷新自己绘制的颜色等自定义状态。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion

    #region 账户和视图事件

    /// <summary>账户资料或登录状态变更后触发，e.User 为新账户快照，可能为空。</summary>
    /// <remarks>订阅权限：UserRead。可从 e.User?.IsLoggedIn 判断登录状态；账户切换时应清理旧账户界面内容。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnUserChanged(object? sender, DriveEvent e)
    {
        // 可从 e.User?.IsLoggedIn 判断登录状态；账户切换时应清理旧账户界面内容。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>自定义视图添加并保存成功后触发，e.View 为新增视图快照。</summary>
    /// <remarks>订阅权限：UserRead。批量创建视图会逐个通知；在此读取 e.View，处理前检查是否为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnViewAdded(object? sender, DriveEvent e)
    {
        // 批量创建视图会逐个通知；在此读取 e.View，处理前检查是否为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>自定义视图删除并保存成功后触发，e.View 为删除前快照。</summary>
    /// <remarks>订阅权限：UserRead。视图名称不是稳定的唯一 ID；保存失败不触发此事件。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnViewDeleted(object? sender, DriveEvent e)
    {
        // 视图名称不是稳定的唯一 ID；保存失败不触发此事件。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion

    #region 网盘目录、文件、搜索和分享事件

    /// <summary>文件页当前目录 ID 变化时触发，e.FolderId 是新目录 ID。</summary>
    /// <remarks>订阅权限：FileRead。这是导航通知，不代表目录已加载完成；根目录 ID 可能为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFolderChanged(object? sender, DriveEvent e)
    {
        // 这是导航通知，不代表目录已加载完成；根目录 ID 可能为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>宿主观察到创建目录、上传保存或合并文件成功后触发。</summary>
    /// <remarks>订阅权限：FileRead。读取 e.FolderId、e.FileIds、e.FolderIds；文件 ID 可能未知，新建目录也会另发 FolderCreated，避免重复业务处理。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileCreated(object? sender, DriveEvent e)
    {
        // 读取 e.FolderId、e.FileIds、e.FolderIds；文件 ID 可能未知，新建目录也会另发 FolderCreated，避免重复业务处理。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>新建目录成功后触发，e.FolderId 为父目录，e.FolderIds 为新目录 ID，e.Name 为名称。</summary>
    /// <remarks>订阅权限：FileRead。服务器未返回目录 ID 时 e.FolderIds 为空；同时也会发送兼容用的 FileCreated。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFolderCreated(object? sender, DriveEvent e)
    {
        // 服务器未返回目录 ID 时 e.FolderIds 为空；同时也会发送兼容用的 FileCreated。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>文件或目录重命名成功后触发，e.Name 为新名称。</summary>
    /// <remarks>订阅权限：FileRead。e.FileIds 与 e.FolderIds 区分目标类型；事件不提供旧名称。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileRenamed(object? sender, DriveEvent e)
    {
        // e.FileIds 与 e.FolderIds 区分目标类型；事件不提供旧名称。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>一批文件或目录移动成功后触发，e.FolderId 为目标目录。</summary>
    /// <remarks>订阅权限：FileRead。读取 e.FileIds 与 e.FolderIds，分别处理移动的文件和目录。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFilesMoved(object? sender, DriveEvent e)
    {
        // 读取 e.FileIds 与 e.FolderIds，分别处理移动的文件和目录。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>一批文件或目录删除结束后触发，只包含确认删除成功的目标。</summary>
    /// <remarks>订阅权限：FileRead。批量读取 e.FileIds、e.FolderIds；部分失败不会回滚成功项，删除目录不枚举后代。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFilesDeleted(object? sender, DriveEvent e)
    {
        // 批量读取 e.FileIds、e.FolderIds；部分失败不会回滚成功项，删除目录不枚举后代。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>文件复制成功后触发，e.FolderId 为目标目录。</summary>
    /// <remarks>订阅权限：FileRead。e.FileIds 当前是源文件 ID；需要新副本 ID 时查询目标目录。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileCopied(object? sender, DriveEvent e)
    {
        // e.FileIds 当前是源文件 ID；需要新副本 ID 时查询目标目录。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>宿主开始打开或预览文件时触发，e.FileIds 和 e.Selection 标识文件。</summary>
    /// <remarks>订阅权限：FileRead。此通知不表示预览器加载成功、播放完成或窗口已关闭。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileOpened(object? sender, DriveEvent e)
    {
        // 此通知不表示预览器加载成功、播放完成或窗口已关闭。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>用户双击文件条目时触发，e.Selection 为条目快照，e.FileIds 为文件 ID。</summary>
    /// <remarks>订阅权限：FileRead。只通知插件，不能拦截默认打开/预览；在此按文件类型处理自己的业务。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileDoubleClicked(object? sender, DriveEvent e)
    {
        // 只通知插件，不能拦截默认打开/预览；在此按文件类型处理自己的业务。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>文件页选中的文件或目录变化时触发，e.Selection 为选区快照。</summary>
    /// <remarks>订阅权限：FileRead。取消全部选择时数组为空；不要修改快照来尝试改变宿主选区。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnSelectionChanged(object? sender, DriveEvent e)
    {
        // 取消全部选择时数组为空；不要修改快照来尝试改变宿主选区。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>一次文件搜索成功返回后触发，e.Search 为搜索条件和结果摘要。</summary>
    /// <remarks>订阅权限：FileRead。先检查 e.Search 是否为空；最多提供 200 条结果，用 ResultsTruncated 判断截断。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileSearched(object? sender, DriveEvent e)
    {
        // 先检查 e.Search 是否为空；最多提供 200 条结果，用 ResultsTruncated 判断截断。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>分享链接创建成功后触发，e.ShareLink 为分享快照。</summary>
    /// <remarks>订阅权限：FileRead。ShareKey 是分享密钥，ShareId 是分享记录 ID，两者不同；创建时 ShareId 可能为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnShareLinkCreated(object? sender, DriveEvent e)
    {
        // ShareKey 是分享密钥，ShareId 是分享记录 ID，两者不同；创建时 ShareId 可能为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>分享设置更新成功后触发，e.ShareLink 为更新后的已知设置。</summary>
    /// <remarks>订阅权限：FileRead。先检查 e.ShareLink 是否为空；快照不包含密码明文，FileId 也可能未知。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnShareLinkUpdated(object? sender, DriveEvent e)
    {
        // 先检查 e.ShareLink 是否为空；快照不包含密码明文，FileId 也可能未知。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>分享链接删除成功后触发，e.ShareLink 为删除前已知快照。</summary>
    /// <remarks>订阅权限：FileRead。使用 ShareId 标识分享；只有宿主已知文件关联时 e.FileIds 才有值。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnShareLinkDeleted(object? sender, DriveEvent e)
    {
        // 使用 ShareId 标识分享；只有宿主已知文件关联时 e.FileIds 才有值。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion

    #region 上传事件

    /// <summary>上传任务被接受并加入队列后触发，e.Transfer 为任务快照。</summary>
    /// <remarks>订阅权限：UploadRead。加入队列不表示已发送数据；e.FolderId 为上传目录，先检查 e.Transfer 是否为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnUploadStarted(object? sender, DriveEvent e)
    {
        // 加入队列不表示已发送数据；e.FolderId 为上传目录，先检查 e.Transfer 是否为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>上传任务成功完成后触发，e.Transfer 为 Completed 状态。</summary>
    /// <remarks>订阅权限：UploadRead。e.FolderId 为目标目录；Transfer.FileId 可能为空，需要时重新查询目录。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileUploaded(object? sender, DriveEvent e)
    {
        // e.FolderId 为目标目录；Transfer.FileId 可能为空，需要时重新查询目录。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>上传任务失败时触发，e.Transfer 为 Failed 状态。</summary>
    /// <remarks>订阅权限：UploadRead。读取 e.Transfer?.Error 获取可用的失败原因，原因可能为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnUploadFailed(object? sender, DriveEvent e)
    {
        // 读取 e.Transfer?.Error 获取可用的失败原因，原因可能为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>上传任务取消时触发，e.Transfer 为 Cancelled 状态。</summary>
    /// <remarks>订阅权限：UploadRead。e.FolderId 为上传目录；取消不保证已清理服务器上的临时数据。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnUploadCancelled(object? sender, DriveEvent e)
    {
        // e.FolderId 为上传目录；取消不保证已清理服务器上的临时数据。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion

    #region 下载事件

    /// <summary>下载任务被接受并加入队列后触发，e.Transfer 为任务快照。</summary>
    /// <remarks>订阅权限：DownloadRead。加入队列不表示已经收到文件内容；先检查 e.Transfer 是否为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnDownloadStarted(object? sender, DriveEvent e)
    {
        // 加入队列不表示已经收到文件内容；先检查 e.Transfer 是否为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>下载任务成功完成后触发，e.Transfer 为 Completed 状态。</summary>
    /// <remarks>订阅权限：DownloadRead。e.Transfer?.FileId 关联网盘文件；事件不含本地路径，GetSaveDirectoryAsync 返回当前配置目录而非历史任务路径。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnFileDownloaded(object? sender, DriveEvent e)
    {
        // e.Transfer?.FileId 关联网盘文件；事件不含本地路径，GetSaveDirectoryAsync 返回当前配置目录而非历史任务路径。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>下载任务失败时触发，e.Transfer 为 Failed 状态。</summary>
    /// <remarks>订阅权限：DownloadRead。读取 e.Transfer?.Error 获取可用的失败原因，原因可能为空。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnDownloadFailed(object? sender, DriveEvent e)
    {
        // 读取 e.Transfer?.Error 获取可用的失败原因，原因可能为空。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    /// <summary>下载任务取消时触发，e.Transfer 为 Cancelled 状态。</summary>
    /// <remarks>订阅权限：DownloadRead。取消不保证本地临时文件已被删除；按自己的业务刷新进度状态。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnDownloadCancelled(object? sender, DriveEvent e)
    {
        // 取消不保证本地临时文件已被删除；按自己的业务刷新进度状态。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion

    #region 菜单点击事件

    /// <summary>用户点击本插件注册的工具栏、文件菜单或目录菜单时触发。</summary>
    /// <remarks>订阅权限：UiMenu、UiFileMenu、UiFolderMenu 中至少一项。按 e.ActionId 区分注册的操作，e.Selection 为条目快照；必须先注册菜单，每轮 OnEnable 重新注册。应用卡片打开按钮走 OnAppActivated。</remarks>
    /// <param name="sender">当前插件的 PluginEvents 实例。</param>
    /// <param name="e">宿主事件快照；e.Time 为事件产生时间，未适用的字段可能为空。</param>
    private void OnActionInvoked(object? sender, DriveEvent e)
    {
        // 按 e.ActionId 区分注册的操作，e.Selection 为条目快照；必须先注册菜单，每轮 OnEnable 重新注册。应用卡片打开按钮走 OnAppActivated。
        // 在此添加你的业务逻辑；调试日志可按需删除。
        Drive.Logger.Info("收到宿主事件：" + e.Id);
    }

    #endregion
}

