using System.Text.Json.Serialization;

namespace Drive.Plugin.SDK.Protocol;

/// <summary>
/// 宿主请求的纯数据载荷，由类型化 SDK 方法填充。
/// </summary>
/// <remarks>仅对应 HostOperation 使用的字段有意义。字段只允许兼容性追加，修改已有含义会破坏协议；普通插件应使用 FilesApi 等接口。</remarks>
public sealed record HostRequest
{
    /// <summary>
    /// 单个网盘文件 ID；重命名目录时也用于目录 ID。
    /// </summary>
    public string FileId { get; init; } = "";
    /// <summary>
    /// 目录操作的父目录或目标目录 ID；适用的操作中空值表示账户根目录。
    /// </summary>
    public string FolderId { get; init; } = "";
    /// <summary>
    /// 新建或重命名条目的名称。
    /// </summary>
    public string Name { get; init; } = "";
    /// <summary>
    /// 重命名目标是否为目录。
    /// </summary>
    public bool IsFolder { get; init; }
    /// <summary>
    /// 批量操作涉及的网盘文件 ID。
    /// </summary>
    public string[] FileIds { get; init; } = [];
    /// <summary>
    /// 批量操作涉及的网盘目录 ID。
    /// </summary>
    public string[] FolderIds { get; init; } = [];
    /// <summary>
    /// 目录文件页码，从 1 开始。
    /// </summary>
    public int PageIndex { get; init; } = 1;
    /// <summary>
    /// 目录文件页大小，宿主允许 1–200。
    /// </summary>
    public int PageSize { get; init; } = 50;
    /// <summary>
    /// 服务端搜索关键词。
    /// </summary>
    public string Query { get; init; } = "";
    /// <summary>
    /// 搜索请求的 FileType 筛选值，具体匹配规则由服务端约定。
    /// </summary>
    public string[] Extensions { get; init; } = [];
    /// <summary>
    /// 搜索是否启用服务端语义模式。
    /// </summary>
    public bool Semantic { get; init; }
    /// <summary>
    /// 读取整个文件时允许的最大字节数，宿主上限为 4 MiB。
    /// </summary>
    public int MaxBytes { get; init; } = 1024 * 1024;
    /// <summary>
    /// 上传文件的本地路径。
    /// </summary>
    public string LocalPath { get; init; } = "";
    /// <summary>
    /// 上传或下载任务 ID。
    /// </summary>
    public string TaskId { get; init; } = "";
    /// <summary>
    /// 通知或确认框标题。
    /// </summary>
    public string Title { get; init; } = "";
    /// <summary>
    /// 操作文本；用于通知正文、确认正文或页面导航路径。
    /// </summary>
    public string Text { get; init; } = "";
    /// <summary>
    /// 宿主导航目标页面。
    /// </summary>
    public HostPage Page { get; init; }
    /// <summary>
    /// 要注册或更新的菜单定义，仅菜单注册使用。
    /// </summary>
    public PluginAction? Action { get; init; }
    /// <summary>
    /// 存储键名或待注销的菜单 ID。
    /// </summary>
    public string Key { get; init; } = "";
    /// <summary>
    /// 写入存储的字符串；底层协议收到 null 时按空字符串处理。
    /// </summary>
    public string? Value { get; init; }
}

/// <summary>
/// 宿主成功响应的纯数据载荷，由请求调度器转换为类型化结果。
/// </summary>
/// <remarks>响应字段随操作变化；错误码和错误文本通过进程通信信封的错误字段传递，不放入此对象。</remarks>
public sealed record HostResponse
{
    /// <summary>
    /// 操作返回的文本、ID、下载链接或存储值；存储键不存在时为 null。
    /// </summary>
    public string? Text { get; init; }
    /// <summary>
    /// 确认框的用户选择，或无专门数据的操作是否成功。
    /// </summary>
    public bool Result { get; init; }
    /// <summary>
    /// 文件详情；非文件详情响应通常为 null。
    /// </summary>
    public FileEntry? File { get; init; }
    /// <summary>
    /// 目录列表或搜索结果；其他响应通常为 null。
    /// </summary>
    public FilePage? Page { get; init; }
    /// <summary>
    /// 当前账户摘要；其他响应通常为 null。
    /// </summary>
    public UserInfo? User { get; init; }
    /// <summary>
    /// 存储容量摘要；其他响应通常为 null。
    /// </summary>
    public StorageCapacity? Capacity { get; init; }
    /// <summary>
    /// 上传或下载任务快照数组。
    /// </summary>
    public TransferInfo[] Tasks { get; init; } = [];
    /// <summary>
    /// 宿主环境快照；其他响应通常为 null。
    /// </summary>
    public HostSystemInfo? System { get; init; }
    /// <summary>
    /// 完整小文件内容；JSON 传输时由序列化器编码为 Base64。
    /// </summary>
    public byte[] Data { get; init; } = [];
    /// <summary>
    /// 插件存储的键名列表。
    /// </summary>
    public string[] Keys { get; init; } = [];
}

/// <summary>
/// SDK 与宿主协议使用的 JSON 源生成上下文，避免 NativeAOT 运行时反射序列化。
/// </summary>
/// <remarks>属性名采用 camelCase；枚举沿用默认数值编码。为宿主与 SDK 的共享协议基础设施，插件通常无需直接使用。</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(HostRequest))]
[JsonSerializable(typeof(HostResponse))]
[JsonSerializable(typeof(DriveEvent))]
[JsonSerializable(typeof(string[]))]
public partial class PluginJsonContext : JsonSerializerContext;
