using Drive.Plugin.Abi;

namespace Drive.Plugin.SDK;

/// <summary>
/// 声明插件卡片元数据和运行能力，供独立运行器读取。
/// </summary>
/// <remarks>同一 DLL 只能有一个带此特性的公开 DrivePlugin 派生类。元数据在编译时写入插件，扫描时无需实例化入口类。</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DrivePluginAttribute : Attribute
{
    /// <summary>
    /// 插件唯一 ID，1–128 个字符；以 ASCII 字母或数字开头，其余可用字母、数字、点、下划线和连字符。建议使用反向域名。
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// 应用卡片和日志显示的插件名称，不能为空，最多 128 个字符。
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 插件版本，例如 1.0.0；必须可由 System.Version 解析，不支持预发布后缀。
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>
    /// 插件作者的显示名称。
    /// </summary>
    public string Author { get; set; } = "";
    /// <summary>
    /// 插件简介，显示在应用卡片中。
    /// </summary>
    public string Description { get; set; } = "";
    /// <summary>
    /// 支持的最低 SDK 协议版本，包含此版本；高 16 位为主版本，低 16 位为次版本。
    /// </summary>
    public uint MinSdkVersion { get; set; } = AbiVersions.Sdk;
    /// <summary>
    /// 支持的最高 SDK 协议版本，包含此版本；默认支持当前主版本下的所有次版本。
    /// </summary>
    public uint MaxSdkVersion { get; set; } = 0x0002_FFFF;
    /// <summary>
    /// 声明的运行能力；HasUi 还要求 UiApplication 权限和所支持平台的 UI 标志。
    /// </summary>
    public PluginCapabilities Capabilities { get; set; }
    /// <summary>
    /// 插件展示类别；本字段本身不授予权限。
    /// </summary>
    public PluginKind Kind { get; set; } = PluginKind.Application;
    /// <summary>
    /// ICO 文件原始字节的 Base64 常量；解码后最多 256 KiB。空字符串表示无图标，不是文件路径或 data URL。
    /// </summary>
    public string IconBase64 { get; set; } = "";
    /// <summary>
    /// 应用卡片图标底色，支持 #RRGGBB 或 #AARRGGBB，默认 #2B80F2。
    /// </summary>
    public string BackgroundColor { get; set; } = "#2B80F2";
    /// <summary>
    /// 应用卡片标签，最多 8 个；每个非空且最多 24 个字符。
    /// </summary>
    public string[] Tags { get; set; } = [];
}

/// <summary>
/// 声明插件通过宿主 API 使用的权限。
/// </summary>
/// <remarks>可以重复标注，所有标注的权限按位合并；声明权限不代表自动启用，宿主仍会检查授权与生命周期。</remarks>
/// <param name="permission">本条声明所需的权限组合。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PluginPermissionAttribute(PluginPermission permission) : Attribute
{
    /// <summary>
    /// 本条特性声明的权限标志，可以按位组合多个权限。
    /// </summary>
    public PluginPermission Permission { get; } = permission;
}

/// <summary>
/// 插件身份、兼容范围、声明权限和卡片展示数据的快照。
/// </summary>
/// <remarks>通常由独立运行器读取特性后创建。数组内容应视为只读；Permissions 表示声明的权限，而非某一时刻宿主是否允许调用。</remarks>
public sealed record PluginInfo
{
    /// <summary>
    /// 插件唯一 ID，也是菜单与持久化存储的隔离标识。
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// 应用卡片和日志显示的插件名称，不能为空，最多 128 个字符。
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// 插件版本，例如 1.0.0；必须可由 System.Version 解析，不支持预发布后缀。
    /// </summary>
    public string Version { get; init; } = "1.0.0";
    /// <summary>
    /// 插件作者的显示名称。
    /// </summary>
    public string Author { get; init; } = "";
    /// <summary>
    /// 插件简介，显示在应用卡片中。
    /// </summary>
    public string Description { get; init; } = "";
    /// <summary>
    /// 支持的最低 SDK 协议版本，包含此版本；高 16 位为主版本，低 16 位为次版本。
    /// </summary>
    public uint MinSdkVersion { get; init; } = AbiVersions.Sdk;
    /// <summary>
    /// 支持的最高 SDK 协议版本，包含此版本；默认支持当前主版本下的所有次版本。
    /// </summary>
    public uint MaxSdkVersion { get; init; } = 0x0002_FFFF;
    /// <summary>
    /// 插件声明的权限集合；宿主还会结合启用状态检查实际可调用性。
    /// </summary>
    public PluginPermission Permissions { get; init; }
    /// <summary>
    /// 声明的运行能力；HasUi 还要求 UiApplication 权限和所支持平台的 UI 标志。
    /// </summary>
    public PluginCapabilities Capabilities { get; init; }
    /// <summary>
    /// 插件展示类别；本字段本身不授予权限。
    /// </summary>
    public PluginKind Kind { get; init; }
    /// <summary>
    /// 旧 ABI 1.0 的 PNG 图标字节，供兼容已有插件使用；新插件应提供 IconIco。
    /// </summary>
    public byte[] IconPng { get; init; } = [];
    /// <summary>
    /// ICO 图标的原始字节，最多 256 KiB；空数组表示未提供。
    /// </summary>
    public byte[] IconIco { get; init; } = [];
    /// <summary>
    /// 应用卡片图标底色，支持 #RRGGBB 或 #AARRGGBB，默认 #2B80F2。
    /// </summary>
    public string BackgroundColor { get; init; } = "#2B80F2";
    /// <summary>
    /// 应用卡片标签，最多 8 个；每个非空且最多 24 个字符。
    /// </summary>
    public string[] Tags { get; init; } = [];
}

/// <summary>
/// 网盘文件或目录的轻量信息快照。
/// </summary>
/// <remarks>字段只包含宿主能够提供的数据；默认值或空字符串不代表服务器一定存在相同数据。</remarks>
public sealed record FileEntry
{
    /// <summary>
    /// 文件或目录 ID；不是本地路径。
    /// </summary>
    public string Id { get; init; } = "";
    /// <summary>
    /// 文件名或目录名。
    /// </summary>
    public string Name { get; init; } = "";
    /// <summary>
    /// 是否为目录；false 表示文件。
    /// </summary>
    public bool IsFolder { get; init; }
    /// <summary>
    /// 父目录 ID；宿主未提供父目录时为空。
    /// </summary>
    public string FolderId { get; init; } = "";
    /// <summary>
    /// 文件内容大小，单位为字节；目录通常为 0。
    /// </summary>
    public ulong Size { get; init; }
    /// <summary>
    /// 服务器提供的文件哈希；没有数据时为空，不承诺固定算法。
    /// </summary>
    public string Hash { get; init; } = "";
    /// <summary>
    /// 服务器提供的创建时间；未提供时为默认值，SDK 不额外转换时区。
    /// </summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>
    /// 服务器提供的最后修改时间；目录可能为默认值，SDK 不额外转换时区。
    /// </summary>
    public DateTime ModifiedAt { get; init; }
}

/// <summary>
/// 网盘文件列表或搜索结果快照。
/// </summary>
/// <remarks>Files 与 Folders 分开返回。目录数组不保证按文件分页规则分页；搜索结果不支持通过此对象继续翻页。</remarks>
public sealed record FilePage
{
    /// <summary>
    /// 当前响应中的文件快照；没有文件时为空数组。
    /// </summary>
    public FileEntry[] Files { get; init; } = [];
    /// <summary>
    /// 当前响应中的子目录快照；不包含作为父目录的自身条目。
    /// </summary>
    public FileEntry[] Folders { get; init; } = [];
    /// <summary>
    /// 服务器报告的匹配文件总数，不包含目录数量。
    /// </summary>
    public int TotalFiles { get; init; }
    /// <summary>
    /// 文件页码，从 1 开始；搜索响应当前为 1。
    /// </summary>
    public int PageIndex { get; init; } = 1;
    /// <summary>
    /// 目录请求的文件页大小；搜索响应为返回的文件数量，可能为 0。
    /// </summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// 当前账户的登录状态和公开资料，不包含访问令牌。
/// </summary>
/// <remarks>未登录时 IsLoggedIn 为 false，其余字符串字段通常为空。</remarks>
public sealed record UserInfo
{
    /// <summary>
    /// 是否存在当前登录账户。
    /// </summary>
    public bool IsLoggedIn { get; init; }
    /// <summary>
    /// 账户 ID；未登录时为空。
    /// </summary>
    public string Id { get; init; } = "";
    /// <summary>
    /// 账户登录名，不包含密码或令牌。
    /// </summary>
    public string UserName { get; init; } = "";
    /// <summary>
    /// 账户昵称；未提供时为空。
    /// </summary>
    public string Nickname { get; init; } = "";
    /// <summary>
    /// 当前账户的网盘根目录 ID；未提供时为空。
    /// </summary>
    public string RootFolderId { get; init; } = "";
}

/// <summary>
/// 当前账户的存储配额快照，所有数值以字节为单位。
/// </summary>
/// <remarks>数值由服务端提供；显示比例时应处理总配额为零的情况。</remarks>
/// <param name="UsedBytes">账户已使用的字节数。</param>
/// <param name="TotalBytes">账户允许使用的总字节数。</param>
public sealed record StorageCapacity(double UsedBytes, double TotalBytes);
/// <summary>
/// 宿主传输任务的状态。
/// </summary>
/// <remarks>状态是快照，不保证每次状态变化都有独立事件通知。</remarks>
public enum TransferState
{
    /// <summary>
    /// 已进入队列，等待调度。
    /// </summary>
    Queued,
    /// <summary>
    /// 正在传输。
    /// </summary>
    Running,
    /// <summary>
    /// 已暂停，可在宿主支持时恢复。
    /// </summary>
    Paused,
    /// <summary>
    /// 已成功完成。
    /// </summary>
    Completed,
    /// <summary>
    /// 传输失败，错误详情可能位于 TransferInfo.Error。
    /// </summary>
    Failed,
    /// <summary>
    /// 任务已取消，不承诺已产生的数据被删除。
    /// </summary>
    Cancelled,
}

/// <summary>
/// 上传或下载任务的轻量状态快照。
/// </summary>
/// <remarks>Id 是传输任务 ID，不是网盘文件 ID。上传和下载提供的字段不同；数据不包含本地文件路径。</remarks>
public sealed record TransferInfo
{
    /// <summary>
    /// 宿主传输任务 ID，可用于暂停、继续或取消任务。
    /// </summary>
    public string Id { get; init; } = "";
    /// <summary>
    /// 关联的网盘文件 ID；下载通常提供，上传当前可能为空。
    /// </summary>
    public string FileId { get; init; } = "";
    /// <summary>
    /// 传输文件的显示名称。
    /// </summary>
    public string Name { get; init; } = "";
    /// <summary>
    /// 上传目标目录 ID；下载或宿主未提供时可能为空。
    /// </summary>
    public string FolderId { get; init; } = "";
    /// <summary>
    /// 预期传输的总字节数；未知时可能为 0。
    /// </summary>
    public ulong TotalBytes { get; init; }
    /// <summary>
    /// 截至快照时已传输的字节数。
    /// </summary>
    public ulong TransferredBytes { get; init; }
    /// <summary>
    /// 宿主测得的当前传输速度，单位为字节/秒。
    /// </summary>
    public double BytesPerSecond { get; init; }
    /// <summary>
    /// 快照时的任务状态。
    /// </summary>
    public TransferState State { get; init; }
    /// <summary>
    /// 失败原因；即使状态为 Failed，也可能因宿主未提供原因而为 null 或空字符串。
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// 宿主版本、运行平台和当前界面设置快照。
/// </summary>
/// <remarks>主题和语言之后可能变化；需要随主题更新时订阅 ThemeChanged。</remarks>
public sealed record HostSystemInfo
{
    /// <summary>
    /// 宿主应用程序集版本字符串。
    /// </summary>
    public string HostVersion { get; init; } = "";
    /// <summary>
    /// 宿主 SDK 协议版本；高 16 位为主版本，低 16 位为次版本。
    /// </summary>
    public uint SdkVersion { get; init; } = AbiVersions.Sdk;
    /// <summary>
    /// 宿主报告的操作系统描述字符串，不应作为固定枚举解析。
    /// </summary>
    public string OperatingSystem { get; init; } = "";
    /// <summary>
    /// 宿主进程架构，例如 X64 或 Arm64。
    /// </summary>
    public string Architecture { get; init; } = "";
    /// <summary>
    /// 宿主当前 UI 语言名称，例如 zh-CN；可能为空。
    /// </summary>
    public string Language { get; init; } = "";
    /// <summary>
    /// 当前有效主题，dark 表示深色，light 表示浅色。
    /// </summary>
    public string Theme { get; init; } = "light";
    /// <summary>
    /// 宿主为当前插件创建的临时目录绝对路径；与其他插件隔离，但不自动按账户隔离，也不保证自动清理。
    /// </summary>
    public string PluginTempDirectory { get; init; } = "";
    /// <summary>主程序当前可见窗口的位置快照；无桌面窗口时为 null。用于插件窗口居中，不授予其他界面操作权限。</summary>
    public HostWindowInfo? Window { get; init; }
}

/// <summary>主程序窗口在桌面物理像素坐标中的边界，供独立运行器定位插件窗口。</summary>
public sealed record HostWindowInfo
{
    /// <summary>窗口左侧的物理像素坐标，多显示器环境下可以为负数。</summary>
    public int X { get; init; }
    /// <summary>窗口顶部的物理像素坐标，多显示器环境下可以为负数。</summary>
    public int Y { get; init; }
    /// <summary>窗口宽度，单位为物理像素。</summary>
    public int Width { get; init; }
    /// <summary>窗口高度，单位为物理像素。</summary>
    public int Height { get; init; }
    /// <summary>宿主窗口的显示缩放比例，例如 1.5 表示 150%。</summary>
    public double Scaling { get; init; } = 1;
    /// <summary>宿主进程 ID；原生窗口关联前必须验证，防止句柄失效或被其他进程复用。</summary>
    public int ProcessId { get; init; }
    /// <summary>Windows HWND 的数值；其他平台为 0，不得跨平台解释。</summary>
    public long WindowsHandle { get; init; }
}

/// <summary>
/// 可以通过 UiApi.NavigateAsync 打开的宿主内置页面。
/// </summary>
/// <remarks>枚举值参与协议序列化，不能重排。</remarks>
public enum HostPage
{
    /// <summary>
    /// 我的文件页面。
    /// </summary>
    Files,
    /// <summary>
    /// 文件搜索页面。
    /// </summary>
    Search,
    /// <summary>
    /// 上传和下载任务管理页面。
    /// </summary>
    Transfers,
    /// <summary>
    /// 分享管理页面。
    /// </summary>
    Shares,
    /// <summary>
    /// 存储与使用统计页面。
    /// </summary>
    Statistics,
    /// <summary>
    /// 客户端设置页面。
    /// </summary>
    Settings,
    /// <summary>
    /// 插件应用管理页面。
    /// </summary>
    Plugins,
}

/// <summary>
/// 插件操作在宿主界面中的展示位置。
/// </summary>
/// <remarks>文件和目录菜单分别需要 UiFileMenu、UiFolderMenu，且均需 FileRead；工具栏需要 UiMenu。</remarks>
public enum PluginActionLocation
{
    /// <summary>
    /// 文件条目的菜单，位于分享操作下面；需要 UiFileMenu 和 FileRead 权限。
    /// </summary>
    FileMenu,
    /// <summary>
    /// 目录条目的菜单，位于重命名操作下面；需要 UiFolderMenu 和 FileRead 权限。
    /// </summary>
    FolderMenu,
    /// <summary>
    /// 宿主文件工具栏；需要 UiMenu 权限。
    /// </summary>
    Toolbar,
}

/// <summary>
/// 由插件注册的菜单或工具栏操作定义。
/// </summary>
/// <remarks>通过 UiApi.RegisterActionAsync 注册；点击后触发 PluginEvents.ActionInvoked。</remarks>
public sealed record PluginAction
{
    /// <summary>
    /// 插件内唯一操作 ID，1–64 个字符，仅支持 ASCII 字母、数字、点、下划线和连字符。
    /// </summary>
    public string Id { get; init; } = "";
    /// <summary>
    /// 菜单或工具栏显示文字，非空且最多 64 个字符。
    /// </summary>
    public string Title { get; init; } = "";
    /// <summary>
    /// 操作的展示位置；文件菜单需要 UiFileMenu，目录菜单需要 UiFolderMenu，工具栏需要 UiMenu；前两者还需 FileRead。
    /// </summary>
    public PluginActionLocation Location { get; init; }
    /// <summary>
    /// 文件菜单的文件名后缀筛选，例如 .mp3；忽略大小写，空数组表示不限制。最多 32 项，每项最多 32 个字符；目录和工具栏不使用此筛选。
    /// </summary>
    public string[] Extensions { get; init; } = [];
}

/// <summary>
/// 宿主事件的数据载荷，通过 Id 判断哪些字段有意义。
/// </summary>
/// <remarks>未用于当前事件的字段保持空数组、空字符串或 null。数组是事件快照，应视为只读；具体字段语义见 PluginEvents 对应事件。</remarks>
public sealed class DriveEvent : EventArgs
{
    /// <summary>宿主生成事件的时间，包含时区偏移；通过进程间传输后保持不变。</summary>
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    /// <summary>分享链接创建、更新、删除事件的快照；其他事件为 null。</summary>
    public ShareLinkInfo? ShareLink { get; init; }
    /// <summary>FileSearched 中的搜索条件和结果摘要；其他事件为 null。</summary>
    public FileSearchInfo? Search { get; init; }
    /// <summary>ViewAdded 或 ViewDeleted 中的视图快照；其他事件为 null。</summary>
    public PluginViewInfo? View { get; init; }
    /// <summary>
    /// 事件编号，决定本次应读取哪些字段。
    /// </summary>
    public DriveEventId Id { get; init; }
    /// <summary>
    /// 受影响的文件 ID；FileCopied 中当前为源文件 ID，不能假定是新副本 ID。
    /// </summary>
    public string[] FileIds { get; init; } = [];
    /// <summary>
    /// 受影响的目录 ID；与 FileIds 分开提供。
    /// </summary>
    public string[] FolderIds { get; init; } = [];
    /// <summary>
    /// 关联目录 ID；FolderChanged 为新目录，FilesMoved 为目标目录，上传事件为上传目录；不适用时为空。
    /// </summary>
    public string FolderId { get; init; } = "";
    /// <summary>
    /// 创建目录时的名称或重命名后的新名称；不提供旧名称。
    /// </summary>
    public string Name { get; init; } = "";
    /// <summary>
    /// 选中文件或目录的快照；用于 SelectionChanged、FileOpened 和 ActionInvoked 等事件，可能为空。
    /// </summary>
    public FileEntry[] Selection { get; init; } = [];
    /// <summary>
    /// 传输事件中的任务快照；其他事件通常为 null。
    /// </summary>
    public TransferInfo? Transfer { get; init; }
    /// <summary>
    /// UserChanged 中的新账户资料；其他事件通常为 null。
    /// </summary>
    public UserInfo? User { get; init; }
    /// <summary>
    /// ThemeChanged 中的 dark 或 light；其他事件通常为空。
    /// </summary>
    public string Theme { get; init; } = "";
    /// <summary>
    /// ActionInvoked 中的 PluginAction.Id；应用卡片的打开按钮不使用此字段。
    /// </summary>
    public string ActionId { get; init; } = "";
}

/// <summary>
/// 宿主 API 或插件桥接层返回的可识别错误。
/// </summary>
/// <remarks>用 Error 判断错误类别，不要通过匹配 Message 文本进行业务分支。本地 CancellationToken 取消也可能直接抛出 OperationCanceledException。</remarks>
/// <param name="error">协议定义的错误类别。</param>
/// <param name="message">供日志和用户提示使用的错误详情。</param>
public sealed class PluginException(PluginError error, string message) : Exception(message)
{
    /// <summary>
    /// 稳定的错误类别，例如 PermissionDenied、NotLoggedIn、TooLarge 或 Timeout。
    /// </summary>
    public PluginError Error { get; } = error;
}
