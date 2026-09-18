using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Drive.Plugin.Abi;

/// <summary>
/// ABI 和 SDK 版本，以及跨边界消息的字节上限。
/// </summary>
/// <remarks>版本高 16 位为主版本，低 16 位为次版本；同一主版本通过尾部追加字段扩展。</remarks>
public static class AbiVersions
{
    /// <summary>
    /// 当前 ABI 版本 1.1。
    /// </summary>
    public const uint Abi = 0x0001_0001;
    /// <summary>
    /// 当前托管进程插件 SDK 协议版本 2.0；ABI 1.x 结构仅保留为旧协议定义。
    /// </summary>
    public const uint Sdk = 0x0002_0000;
    /// <summary>
    /// 单次跨边界 JSON 消息的最大长度，8 MiB；不是文件读取上限。
    /// </summary>
    public const int MaxMessageBytes = 8 * 1024 * 1024;
    /// <summary>
    /// 默认元数据字符串读取上限，16 KiB。
    /// </summary>
    public const int MaxMetadataBytes = 16 * 1024;
    /// <summary>
    /// 单个插件图标最大字节数，256 KiB。
    /// </summary>
    public const int MaxIconBytes = 256 * 1024;
    /// <summary>
    /// 判断指定 ABI 是否与当前 ABI 主版本兼容。
    /// </summary>
    /// <param name="abi">待检查的 ABI 编码版本。</param>
    /// <returns>主版本相同则为 true；不检查 SDK 范围或结构大小。</returns>
    public static bool IsCompatible(uint abi)
    {
        return (abi >> 16) == (Abi >> 16);
    }
}

/// <summary>
/// 跨 C ABI 传递的错误码。
/// </summary>
/// <remarks>数值属于公开协议，不能重排或复用；0 表示成功。</remarks>
public enum PluginError : int
{
    /// <summary>
    /// 操作成功。
    /// </summary>
    Success = 0,
    /// <summary>
    /// 参数为空、格式不正确或超出允许范围。
    /// </summary>
    InvalidArgument = 1,
    /// <summary>
    /// ABI 主版本、SDK 版本范围或结构大小不兼容。
    /// </summary>
    IncompatibleVersion = 2,
    /// <summary>
    /// 插件没有声明或未获准使用所需权限。
    /// </summary>
    PermissionDenied = 3,
    /// <summary>
    /// 插件尚未初始化、已停用或宿主已撤销其调用能力。
    /// </summary>
    NotEnabled = 4,
    /// <summary>
    /// 当前操作需要登录账户或可用的主窗口。
    /// </summary>
    NotLoggedIn = 5,
    /// <summary>
    /// 请求的文件、任务、窗口或其他资源不存在。
    /// </summary>
    NotFound = 6,
    /// <summary>
    /// 宿主或当前平台不支持该操作。
    /// </summary>
    Unsupported = 7,
    /// <summary>
    /// 操作被宿主取消，例如账户切换；已发送的服务端操作不保证回滚。
    /// </summary>
    Cancelled = 8,
    /// <summary>
    /// 等待操作或回调超时；不保证服务端操作已经终止。
    /// </summary>
    Timeout = 9,
    /// <summary>
    /// 宿主请求队列已满或当前无法接受更多工作。
    /// </summary>
    Busy = 10,
    /// <summary>
    /// 未归入其他类别的执行失败，详情见错误消息。
    /// </summary>
    Failed = 11,
    /// <summary>
    /// 请求、响应、文件内容或存储用量超过限制。
    /// </summary>
    TooLarge = 12,
    /// <summary>
    /// 重复初始化、重复请求 ID 或其他唯一标识冲突。
    /// </summary>
    Duplicate = 13,
}

/// <summary>
/// 插件声明的宿主 API 访问权限位标志。
/// </summary>
/// <remarks>可按位组合。权限用于宿主 API 访问控制，不是原生 DLL 的系统安全沙箱。</remarks>
[Flags]
public enum PluginPermission : ulong
{
    /// <summary>
    /// 不声明额外宿主权限。
    /// </summary>
    None = 0,
    /// <summary>
    /// 查询文件与目录、搜索、读取小文件、获取临时下载链接，并订阅文件事件。
    /// </summary>
    FileRead = 1UL << 0,
    /// <summary>
    /// 创建目录、重命名、移动或复制网盘条目。
    /// </summary>
    FileWrite = 1UL << 1,
    /// <summary>
    /// 删除网盘文件或目录。
    /// </summary>
    FileDelete = 1UL << 2,
    /// <summary>
    /// 向宿主添加上传任务。
    /// </summary>
    UploadCreate = 1UL << 3,
    /// <summary>
    /// 查询当前账户上传任务，并订阅上传事件。
    /// </summary>
    UploadRead = 1UL << 4,
    /// <summary>
    /// 暂停、恢复或取消当前账户上传任务。
    /// </summary>
    UploadControl = 1UL << 5,
    /// <summary>
    /// 向宿主添加下载任务。
    /// </summary>
    DownloadCreate = 1UL << 6,
    /// <summary>
    /// 查询当前账户下载任务、读取客户端配置的下载保存目录，并订阅下载事件。
    /// </summary>
    DownloadRead = 1UL << 7,
    /// <summary>
    /// 暂停、恢复或取消当前账户下载任务。
    /// </summary>
    DownloadControl = 1UL << 8,
    /// <summary>
    /// 读取当前账户摘要和空间配额，并订阅账户事件。
    /// </summary>
    UserRead = 1UL << 9,
    /// <summary>
    /// 在宿主显示通知或确认框。
    /// </summary>
    UiNotification = 1UL << 10,
    /// <summary>
    /// 注册、移除文件页工具栏操作，并接收其点击事件；文件菜单和目录菜单需分别申请 UiFileMenu、UiFolderMenu。
    /// </summary>
    UiMenu = 1UL << 11,
    /// <summary>
    /// 提供插件应用入口、导航宿主页面或调用文件预览；预览还需 FileRead。
    /// </summary>
    UiApplication = 1UL << 12,
    /// <summary>
    /// 读写当前插件隔离的持久化键值存储。
    /// </summary>
    Storage = 1UL << 13,
    /// <summary>
    /// 在文件条目的分享操作下面注册菜单并接收点击事件；还需 FileRead 权限。
    /// </summary>
    UiFileMenu = 1UL << 14,
    /// <summary>
    /// 在文件夹条目的重命名操作下面注册菜单并接收点击事件；还需 FileRead 权限。
    /// </summary>
    UiFolderMenu = 1UL << 15,
    /// <summary>
    /// 当前协议所有已定义权限的组合；建议插件只声明实际需要的最小权限。
    /// </summary>
    AllSupported = (1UL << 16) - 1,
    /// <summary>
    /// 一次申请当前 SDK 定义的全部宿主权限，包括文件、传输、账户、界面、菜单和插件存储。
    /// </summary>
    /// <remarks>
    /// 在插件入口使用 [PluginPermission(PluginPermission.All)] 声明；与 AllSupported 等价。
    /// 仍需用户启用授权，不绕过登录、生命周期、平台能力或接口参数检查。
    /// 该值在插件编译时确定；未来 SDK 增加权限后，需要重新编译插件才能包含新增权限。
    /// </remarks>
    All = AllSupported,
}

/// <summary>
/// 插件声明的运行与平台界面能力位标志。
/// </summary>
/// <remarks>能力描述不替代权限声明；界面可用性还由宿主和平台检查。</remarks>
[Flags]
public enum PluginCapabilities : ulong
{
    /// <summary>
    /// 不声明额外运行能力。
    /// </summary>
    None = 0,
    /// <summary>
    /// 插件提供应用入口，需要同时声明 UiApplication 权限及平台 UI 能力。
    /// </summary>
    HasUi = 1,
    /// <summary>
    /// 插件声明可执行后台任务；不额外授予宿主权限。
    /// </summary>
    Background = 2,
    /// <summary>
    /// 插件声明支持 Windows 界面。
    /// </summary>
    WindowsUi = 4,
    /// <summary>
    /// 插件声明支持 Linux 界面；不代表窗口辅助库已支持该平台。
    /// </summary>
    LinuxUi = 8,
    /// <summary>
    /// 插件声明支持 macOS 界面；不代表窗口辅助库已支持该平台。
    /// </summary>
    MacOsUi = 16,
}

/// <summary>
/// 插件的展示类别。
/// </summary>
/// <remarks>固定数值属于公开协议。</remarks>
public enum PluginKind : uint
{
    /// <summary>
    /// 以插件应用展示。
    /// </summary>
    Application = 0,
    /// <summary>
    /// 以后台功能插件展示。
    /// </summary>
    Background = 1,
    /// <summary>
    /// 以文件工具插件展示。
    /// </summary>
    FileTool = 2,
}

/// <summary>
/// 宿主异步请求的操作编号。
/// </summary>
/// <remarks>数值属于公开协议，不能重排或复用；普通插件通过 SDK 的类型化 API 调用。</remarks>
public enum HostOperation : uint
{
    /// <summary>
    /// 分页查询目录内容。
    /// </summary>
    FileList = 100,
    /// <summary>
    /// 查询一个网盘文件的详情。
    /// </summary>
    FileGet = 101,
    /// <summary>
    /// 调用服务端文件搜索。
    /// </summary>
    FileSearch = 102,
    /// <summary>
    /// 创建网盘目录。
    /// </summary>
    FolderCreate = 103,
    /// <summary>
    /// 重命名文件或目录。
    /// </summary>
    FileRename = 104,
    /// <summary>
    /// 移动文件或目录。
    /// </summary>
    FileMove = 105,
    /// <summary>
    /// 删除文件或目录。
    /// </summary>
    FileDelete = 106,
    /// <summary>
    /// 复制一个文件。
    /// </summary>
    FileCopy = 107,
    /// <summary>
    /// 获取临时下载地址。
    /// </summary>
    FileDownloadUrl = 108,
    /// <summary>
    /// 在大小上限内读取文件的完整内容。
    /// </summary>
    FileRead = 109,
    /// <summary>
    /// 查询文件页当前目录 ID。
    /// </summary>
    CurrentFolder = 110,
    /// <summary>
    /// 创建上传任务。
    /// </summary>
    UploadStart = 200,
    /// <summary>
    /// 查询上传任务快照。
    /// </summary>
    UploadList = 201,
    /// <summary>
    /// 暂停上传任务。
    /// </summary>
    UploadPause = 202,
    /// <summary>
    /// 恢复上传任务。
    /// </summary>
    UploadResume = 203,
    /// <summary>
    /// 取消上传任务。
    /// </summary>
    UploadCancel = 204,
    /// <summary>
    /// 创建下载任务。
    /// </summary>
    DownloadStart = 300,
    /// <summary>
    /// 查询下载任务快照。
    /// </summary>
    DownloadList = 301,
    /// <summary>
    /// 暂停下载任务。
    /// </summary>
    DownloadPause = 302,
    /// <summary>
    /// 恢复下载任务。
    /// </summary>
    DownloadResume = 303,
    /// <summary>
    /// 取消下载任务。
    /// </summary>
    DownloadCancel = 304,
    /// <summary>
    /// 读取客户端当前配置的下载保存目录，不要求登录。
    /// </summary>
    DownloadSaveDirectory = 305,
    /// <summary>
    /// 查询当前账户摘要，允许返回未登录状态。
    /// </summary>
    UserCurrent = 400,
    /// <summary>
    /// 查询账户存储配额。
    /// </summary>
    UserStorage = 401,
    /// <summary>
    /// 显示宿主通知。
    /// </summary>
    UiNotify = 500,
    /// <summary>
    /// 显示宿主确认框并返回用户选择。
    /// </summary>
    UiConfirm = 501,
    /// <summary>
    /// 注册或更新插件菜单操作。
    /// </summary>
    UiRegisterAction = 502,
    /// <summary>
    /// 移除插件菜单操作。
    /// </summary>
    UiUnregisterAction = 503,
    /// <summary>
    /// 导航至宿主内置页面。
    /// </summary>
    UiNavigate = 504,
    /// <summary>
    /// 调用宿主文件预览或打开流程。
    /// </summary>
    UiOpenFile = 505,
    /// <summary>
    /// 读取插件存储值。
    /// </summary>
    StorageGet = 600,
    /// <summary>
    /// 创建或覆盖插件存储值。
    /// </summary>
    StorageSet = 601,
    /// <summary>
    /// 删除插件存储键。
    /// </summary>
    StorageDelete = 602,
    /// <summary>
    /// 列出插件存储键名。
    /// </summary>
    StorageEnumerate = 603,
    /// <summary>
    /// 查询宿主运行环境。
    /// </summary>
    SystemInfo = 700,
}

/// <summary>
/// 宿主事件的协议编号。
/// </summary>
/// <remarks>数值属于公开协议，不能重排或复用；对应数据字段与线程规则见 SDK 的 PluginEvents。</remarks>
public enum DriveEventId : uint
{
    /// <summary>
    /// 插件本轮启用成功后触发。
    /// </summary>
    ApplicationStarted = 1,
    /// <summary>
    /// 宿主正常关闭时，在停用当前已启用插件之前触发。
    /// </summary>
    ApplicationStopping = 2,
    /// <summary>
    /// 宿主账户资料或登录状态变更时触发。
    /// </summary>
    UserChanged = 10,
    /// <summary>
    /// 宿主文件页当前目录 ID 变更时触发。
    /// </summary>
    FolderChanged = 11,
    /// <summary>
    /// 宿主切换明暗主题时触发。
    /// </summary>
    ThemeChanged = 12,
    /// <summary>
    /// 宿主观察到创建目录、上传保存或合并文件成功时触发。
    /// </summary>
    FileCreated = 100,
    /// <summary>
    /// 网盘文件或目录重命名成功后触发。
    /// </summary>
    FileRenamed = 101,
    /// <summary>
    /// 一批网盘文件或目录移动成功后触发。
    /// </summary>
    FilesMoved = 102,
    /// <summary>
    /// 宿主确认一批文件或目录删除成功后触发。
    /// </summary>
    FilesDeleted = 103,
    /// <summary>
    /// 宿主观察到网盘文件复制成功后触发。
    /// </summary>
    FileCopied = 104,
    /// <summary>
    /// 宿主开始执行文件打开或预览流程时触发。
    /// </summary>
    FileOpened = 105,
    /// <summary>
    /// 宿主文件页选中的文件或目录变化时触发。
    /// </summary>
    SelectionChanged = 106,
    /// <summary>新建网盘目录成功后触发；保留原 FileCreated 通知以兼容已有插件。</summary>
    FolderCreated = 107,
    /// <summary>用户双击文件条目时通知插件；不阻止宿主原有的打开或预览。</summary>
    FileDoubleClicked = 108,
    /// <summary>宿主文件搜索成功返回后触发，包括普通搜索和语义搜索。</summary>
    FileSearched = 109,
    /// <summary>文件分享链接创建成功后触发。</summary>
    ShareLinkCreated = 110,
    /// <summary>文件分享链接设置更新成功后触发。</summary>
    ShareLinkUpdated = 111,
    /// <summary>文件分享链接删除成功后触发。</summary>
    ShareLinkDeleted = 112,
    /// <summary>当前账户的自定义视图添加并保存成功后触发。</summary>
    ViewAdded = 400,
    /// <summary>当前账户的自定义视图删除并保存成功后触发。</summary>
    ViewDeleted = 401,
    /// <summary>
    /// 一个上传任务被宿主接受并加入队列时触发。
    /// </summary>
    UploadStarted = 200,
    /// <summary>
    /// 一个上传任务成功完成后触发。
    /// </summary>
    FileUploaded = 201,
    /// <summary>
    /// 一个上传任务失败时触发。
    /// </summary>
    UploadFailed = 202,
    /// <summary>
    /// 一个上传任务被取消时触发。
    /// </summary>
    UploadCancelled = 203,
    /// <summary>
    /// 一个下载任务被宿主接受并加入队列时触发。
    /// </summary>
    DownloadStarted = 300,
    /// <summary>
    /// 一个下载任务成功完成后触发。
    /// </summary>
    FileDownloaded = 301,
    /// <summary>
    /// 一个下载任务失败时触发。
    /// </summary>
    DownloadFailed = 302,
    /// <summary>
    /// 一个下载任务被取消时触发。
    /// </summary>
    DownloadCancelled = 303,
    /// <summary>
    /// 用户点击当前插件注册的菜单或工具栏操作时触发。
    /// </summary>
    ActionInvoked = 500,
}

/// <summary>
/// 跨 C ABI 传递的非托管字节缓冲区视图。
/// </summary>
/// <remarks>Length 的单位是字节，不是字符。不拥有内存，也不会释放指针；普通请求及回调载荷只在相应原生调用期间有效，接收方必须及时复制。元数据切片的生命周期另见 NativePluginInfo。</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeSlice
{
    /// <summary>
    /// 缓冲区首地址；Length 非零时不得为零。
    /// </summary>
    public nint Data;
    /// <summary>
    /// 缓冲区长度，单位为字节；不包含额外的字符串终止符。
    /// </summary>
    public nuint Length;
    /// <summary>
    /// 使用借用的地址和字节数创建视图，不复制或分配内存。
    /// </summary>
    /// <param name="data">可读取的非托管缓冲区首地址。</param>
    /// <param name="length">缓冲区字节数。</param>
    public NativeSlice(nint data, nuint length)
    {
        Data = data;
        Length = length;
    }
}

/// <summary>
/// 插件导出的元数据结构，采用顺序布局和 8 字节对齐。
/// </summary>
/// <remarks>调用前由宿主将 Size 设置为目标缓冲区容量。插件只写入双方已知的前缀；所有字符串切片均为 UTF-8，无结尾零字符要求。切片内存由插件持有至进程结束，宿主只复制、不释放。</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativePluginInfo
{
    /// <summary>
    /// 调用前为宿主缓冲区容量，返回前缀中为插件自身结构大小，单位为字节。
    /// </summary>
    public uint Size;
    /// <summary>
    /// 插件 ABI 版本。
    /// </summary>
    public uint AbiVersion;
    /// <summary>
    /// 插件支持的最低宿主 SDK 版本，包含边界。
    /// </summary>
    public uint MinSdkVersion;
    /// <summary>
    /// 插件支持的最高宿主 SDK 版本，包含边界。
    /// </summary>
    public uint MaxSdkVersion;
    /// <summary>
    /// 插件声明的权限位集合。
    /// </summary>
    public PluginPermission Permissions;
    /// <summary>
    /// 插件声明的运行与平台 UI 能力。
    /// </summary>
    public PluginCapabilities Capabilities;
    /// <summary>
    /// 插件展示类别。
    /// </summary>
    public PluginKind Kind;
    /// <summary>
    /// 保留字段，写入零。
    /// </summary>
    public uint Reserved;
    /// <summary>
    /// UTF-8 编码的插件唯一 ID。
    /// </summary>
    public NativeSlice Id;
    /// <summary>
    /// UTF-8 编码的插件显示名称。
    /// </summary>
    public NativeSlice Name;
    /// <summary>
    /// UTF-8 编码的版本字符串。
    /// </summary>
    public NativeSlice Version;
    /// <summary>
    /// UTF-8 编码的作者名称。
    /// </summary>
    public NativeSlice Author;
    /// <summary>
    /// UTF-8 编码的插件简介。
    /// </summary>
    public NativeSlice Description;
    /// <summary>
    /// 旧 ABI 1.0 的 PNG 图标字节，保留以兼容已有二进制。
    /// </summary>
    public NativeSlice IconPng;
    /// <summary>
    /// ABI 1.1 追加的 ICO 图标原始字节。
    /// </summary>
    public NativeSlice IconIco;
    /// <summary>
    /// ABI 1.1 追加的 UTF-8 颜色字符串，格式为 #RRGGBB 或 #AARRGGBB。
    /// </summary>
    public NativeSlice BackgroundColor;
    /// <summary>
    /// ABI 1.1 追加的 UTF-8 JSON 字符串数组，包含插件标签。
    /// </summary>
    public NativeSlice TagsJson;
    /// <summary>
    /// ABI 1.0 元数据前缀的字节数，不包含 ABI 1.1 追加的三个切片。
    /// </summary>
    public static unsafe uint BaseSize
    {
        get
        {
            return (uint)(sizeof(NativePluginInfo) - 3 * sizeof(NativeSlice));
        }
    }
}

/// <summary>
/// 宿主提供的 Cdecl 函数表，采用顺序布局和 8 字节对齐。
/// </summary>
/// <remarks>函数表指针仅在 Init 调用期间借用，插件必须复制已知前缀。回调函数和上下文在会话期间有效。错误通过 PluginError 返回，托管异常不得跨越原生调用边界。</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public unsafe struct NativeHostApi
{
    /// <summary>
    /// 宿主函数表结构大小，单位为字节。
    /// </summary>
    public uint Size;
    /// <summary>
    /// 宿主 ABI 版本，插件应先检查主版本。
    /// </summary>
    public uint AbiVersion;
    /// <summary>
    /// 宿主提供的 SDK 协议版本。
    /// </summary>
    public uint SdkVersion;
    /// <summary>
    /// 保留字段，必须为零。
    /// </summary>
    public uint Reserved;
    /// <summary>
    /// 宿主能力保留位，当前实现为零。
    /// </summary>
    public ulong Capabilities;
    /// <summary>
    /// 宿主会话上下文；调用各函数时原样传回，不可解引用。
    /// </summary>
    public nint Context;
    /// <summary>
    /// 提交异步请求：参数依次为宿主上下文、非零请求 ID、操作编号、借用的请求字节、完成回调地址、插件回调上下文。返回 0 仅表示接受请求。
    /// </summary>
    /// <remarks>完成回调签名为 void(context, requestId, errorCode, payload)，使用 Cdecl；payload 仅在回调期间有效。成功载荷为 JSON，失败载荷为 UTF-8 错误文本。回调可能早于 Submit 返回，停用时不保证还有回调。</remarks>
    public delegate* unmanaged[Cdecl]<nint, ulong, uint, NativeSlice, nint, nint, int> Submit;
    /// <summary>
    /// 请求取消：参数为宿主上下文和请求 ID，返回 PluginError；取消不保证服务端操作回滚。
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, ulong, int> Cancel;
    /// <summary>
    /// 订阅事件：参数为宿主上下文和事件编号，返回 PluginError；宿主检查权限与生命周期。
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, uint, int> Subscribe;
    /// <summary>
    /// 退订事件：参数为宿主上下文和事件编号，返回 PluginError。
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, uint, int> Unsubscribe;
    /// <summary>
    /// 写日志：参数为宿主上下文、等级和借用的 UTF-8 内容，返回 PluginError；等级 0/1/2 对应信息/警告/错误。
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, int, NativeSlice, int> Log;
}

/// <summary>
/// 宿主操作、事件与所需权限之间的协议映射。
/// </summary>
/// <remarks>多位权限表示全部需要，不是任选其一；未知编号应拒绝处理。</remarks>
public static class PluginPermissions
{
    /// <summary>
    /// 取得宿主操作所需的全部权限位。
    /// </summary>
    /// <param name="operation">已定义的宿主操作编号。</param>
    /// <returns>所需固定权限组合；菜单注册和移除返回 None，宿主还必须检查菜单权限及实际注册位置。</returns>
    /// <exception cref="ArgumentOutOfRangeException">操作编号未定义。</exception>
    public static PluginPermission ForOperation(HostOperation operation)
    {
        return operation switch
        {
            HostOperation.FileList or HostOperation.FileGet or HostOperation.FileSearch or HostOperation.FileDownloadUrl or HostOperation.FileRead or HostOperation.CurrentFolder => PluginPermission.FileRead,
            HostOperation.FolderCreate or HostOperation.FileRename or HostOperation.FileMove or HostOperation.FileCopy => PluginPermission.FileWrite,
            HostOperation.FileDelete => PluginPermission.FileDelete,
            HostOperation.UploadStart => PluginPermission.UploadCreate,
            HostOperation.UploadList => PluginPermission.UploadRead,
            HostOperation.UploadPause or HostOperation.UploadResume or HostOperation.UploadCancel => PluginPermission.UploadControl,
            HostOperation.DownloadStart => PluginPermission.DownloadCreate,
            HostOperation.DownloadList or HostOperation.DownloadSaveDirectory => PluginPermission.DownloadRead,
            HostOperation.DownloadPause or HostOperation.DownloadResume or HostOperation.DownloadCancel => PluginPermission.DownloadControl,
            HostOperation.UserCurrent or HostOperation.UserStorage => PluginPermission.UserRead,
            HostOperation.UiNotify or HostOperation.UiConfirm => PluginPermission.UiNotification,
            HostOperation.UiRegisterAction or HostOperation.UiUnregisterAction => PluginPermission.None,
            HostOperation.UiNavigate => PluginPermission.UiApplication,
            HostOperation.UiOpenFile => PluginPermission.UiApplication | PluginPermission.FileRead,
            HostOperation.StorageGet or HostOperation.StorageSet or HostOperation.StorageDelete or HostOperation.StorageEnumerate => PluginPermission.Storage,
            HostOperation.SystemInfo => PluginPermission.None,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))};
    }

    /// <summary>
    /// 取得订阅一个事件所需的全部权限位。
    /// </summary>
    /// <param name="eventId">已定义的事件编号。</param>
    /// <returns>所需固定权限组合；ActionInvoked 的任选菜单权限需通过 CanSubscribe 检查。</returns>
    /// <exception cref="ArgumentOutOfRangeException">事件编号未定义。</exception>
    public static PluginPermission ForEvent(DriveEventId eventId)
    {
        return eventId switch
        {
            DriveEventId.ApplicationStarted or DriveEventId.ApplicationStopping or DriveEventId.ThemeChanged => PluginPermission.None,
            DriveEventId.UserChanged or DriveEventId.ViewAdded or DriveEventId.ViewDeleted => PluginPermission.UserRead,
            DriveEventId.FolderChanged or DriveEventId.FileCreated or DriveEventId.FileRenamed or DriveEventId.FilesMoved or DriveEventId.FilesDeleted or DriveEventId.FileCopied or DriveEventId.FileOpened or DriveEventId.SelectionChanged or DriveEventId.FolderCreated or DriveEventId.FileDoubleClicked or DriveEventId.FileSearched or DriveEventId.ShareLinkCreated or DriveEventId.ShareLinkUpdated or DriveEventId.ShareLinkDeleted => PluginPermission.FileRead,
            DriveEventId.UploadStarted or DriveEventId.FileUploaded or DriveEventId.UploadFailed or DriveEventId.UploadCancelled => PluginPermission.UploadRead,
            DriveEventId.DownloadStarted or DriveEventId.FileDownloaded or DriveEventId.DownloadFailed or DriveEventId.DownloadCancelled => PluginPermission.DownloadRead,
            DriveEventId.ActionInvoked => PluginPermission.None,
            _ => throw new ArgumentOutOfRangeException(nameof(eventId))};
    }

    /// <summary>
    /// 判断是否声明了工具栏、文件菜单、目录菜单中的任意一种权限。
    /// </summary>
    /// <param name="permissions">插件已获准使用的权限。</param>
    /// <returns>具有至少一种菜单权限时为 true；不代表具有其他位置的注册权限。</returns>
    public static bool HasMenuPermission(PluginPermission permissions)
    {
        return (permissions & (PluginPermission.UiMenu | PluginPermission.UiFileMenu | PluginPermission.UiFolderMenu)) != 0;
    }

    /// <summary>判断权限是否允许订阅指定事件，包括菜单事件的任选权限规则。</summary>
    /// <param name="permissions">插件已获准使用的权限。</param>
    /// <param name="eventId">已定义的事件编号。</param>
    /// <returns>允许订阅时为 true。</returns>
    /// <exception cref="ArgumentOutOfRangeException">事件编号未定义。</exception>
    public static bool CanSubscribe(PluginPermission permissions, DriveEventId eventId)
    {
        var required = ForEvent(eventId);
        return (permissions & required) == required && (eventId != DriveEventId.ActionInvoked || HasMenuPermission(permissions));
    }

    /// <summary>
    /// 将已知权限位转换为宿主显示和诊断使用的协议名称。
    /// </summary>
    /// <param name="permissions">需要描述的权限组合。</param>
    /// <returns>按协议定义顺序排列的名称数组；None 返回空数组，未知权限位不出现在结果中。</returns>
    public static string[] Names(PluginPermission permissions)
    {
        (PluginPermission Value, string Name)[] names = [(PluginPermission.FileRead, "file.read"), (PluginPermission.FileWrite, "file.write"), (PluginPermission.FileDelete, "file.delete"), (PluginPermission.UploadCreate, "upload.create"), (PluginPermission.UploadRead, "upload.read"), (PluginPermission.UploadControl, "upload.control"), (PluginPermission.DownloadCreate, "download.create"), (PluginPermission.DownloadRead, "download.read"), (PluginPermission.DownloadControl, "download.control"), (PluginPermission.UserRead, "user.read"), (PluginPermission.UiNotification, "ui.notification"), (PluginPermission.UiMenu, "ui.menu"), (PluginPermission.UiApplication, "ui.application"), (PluginPermission.Storage, "storage"), (PluginPermission.UiFileMenu, "ui.menu.file"), (PluginPermission.UiFolderMenu, "ui.menu.folder")];
        return names.Where(x => (permissions & x.Value) != 0).Select(x => x.Name).ToArray();
    }
}

/// <summary>
/// 宿主和 SDK 共用的非托管缓冲区复制与严格 UTF-8 解码工具。
/// </summary>
/// <remarks>不会释放借用的内存；调用方必须保证非零指针真实有效并且可读取指定长度。不能用这些长度检查验证任意本机指针的安全性。</remarks>
public static unsafe class NativeUtf8
{
    private static readonly UTF8Encoding Strict = new(false, true);
    /// <summary>
    /// 将借用的非托管字节复制为当前运行时拥有的数组。
    /// </summary>
    /// <param name="slice">有效的缓冲区视图；复制期间内存必须保持有效。</param>
    /// <param name="limit">允许的最大字节数，应为非负数。</param>
    /// <returns>独立的字节数组；零长度视图返回空数组。</returns>
    /// <exception cref="ArgumentException">长度超过限制，或非零长度对应空指针。</exception>
    public static byte[] Copy(NativeSlice slice, int limit = AbiVersions.MaxMessageBytes)
    {
        if (slice.Length > (nuint)limit || (slice.Length != 0 && slice.Data == 0))
            throw new ArgumentException("Invalid native buffer length or pointer.");
        return new ReadOnlySpan<byte>((void*)slice.Data, checked((int)slice.Length)).ToArray();
    }

    /// <summary>
    /// 复制缓冲区并使用严格 UTF-8 解码为字符串。
    /// </summary>
    /// <param name="slice">UTF-8 字节视图，不需要结尾零字符。</param>
    /// <param name="limit">允许读取的最大字节数，默认使用元数据限制。</param>
    /// <returns>解码后的字符串，零长度视图返回空字符串。</returns>
    /// <exception cref="ArgumentException">长度超限，或非零长度对应空指针。</exception>
    /// <exception cref="DecoderFallbackException">缓冲区含非法 UTF-8 字节。</exception>
    public static string Read(NativeSlice slice, int limit = AbiVersions.MaxMetadataBytes)
    {
        return Strict.GetString(Copy(slice, limit));
    }
}
