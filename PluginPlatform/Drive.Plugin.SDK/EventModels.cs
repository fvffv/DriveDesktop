namespace Drive.Plugin.SDK;

/// <summary>分享链接变动时的快照，不包含访问密码。</summary>
/// <remarks>创建接口返回 ShareKey，更新和删除接口使用 ShareId，两者不能互换。接口没有返回的信息保持空值。</remarks>
public sealed record ShareLinkInfo
{
    /// <summary>被分享的文件 ID；删除时若当前客户端尚未读取过分享列表，则可能为空。</summary>
    public string FileId { get; init; } = "";
    /// <summary>分享记录 ID，用于关联更新和删除事件；创建接口未返回该 ID 时为空。</summary>
    public string ShareId { get; init; } = "";
    /// <summary>创建接口返回的分享链接密钥；更新或删除事件通常为空。</summary>
    public string ShareKey { get; init; } = "";
    /// <summary>有效期开始时间，保留服务器接口使用的时间语义；未知时为 null。</summary>
    public DateTime? BeginValidity { get; init; }
    /// <summary>有效期结束时间；未知时为 null。</summary>
    public DateTime? EndValidity { get; init; }
    /// <summary>分享简介；未设置或未知时为空。</summary>
    public string Introduction { get; init; } = "";
    /// <summary>是否设置了密码；未知时为 null。事件不会提供密码明文。</summary>
    public bool? HasPassword { get; init; }
}

/// <summary>一次成功搜索的条件和结果摘要，条件在请求发出时复制。</summary>
public sealed record FileSearchInfo
{
    /// <summary>搜索关键字；未设置时为空。</summary>
    public string Keyword { get; init; } = "";
    /// <summary>是否使用语义搜索。</summary>
    public bool IsSemantic { get; init; }
    /// <summary>文件类型或后缀筛选；空数组表示未限制。</summary>
    public string[] FileTypes { get; init; } = [];
    /// <summary>文件大小下限，单位字节；未限制时为 null。</summary>
    public ulong? MinSize { get; init; }
    /// <summary>文件大小上限，单位字节；未限制时为 null。</summary>
    public ulong? MaxSize { get; init; }
    /// <summary>修改时间下限，保留请求的原始字符串；未限制时为空。</summary>
    public string ModifiedAfter { get; init; } = "";
    /// <summary>修改时间上限，保留请求的原始字符串；未限制时为空。</summary>
    public string ModifiedBefore { get; init; } = "";
    /// <summary>创建时间下限，保留请求的原始字符串；未限制时为空。</summary>
    public string CreatedAfter { get; init; } = "";
    /// <summary>创建时间上限，保留请求的原始字符串；未限制时为空。</summary>
    public string CreatedBefore { get; init; } = "";
    /// <summary>排序条件，保留请求的原始值；空数组表示默认排序。</summary>
    public string[] OrderBy { get; init; } = [];
    /// <summary>服务器报告的匹配文件总数，不包含目录。</summary>
    public long TotalFiles { get; init; }
    /// <summary>本次响应实际返回的文件数量。</summary>
    public int ReturnedFileCount { get; init; }
    /// <summary>本次响应实际返回的目录数量。</summary>
    public int ReturnedFolderCount { get; init; }
    /// <summary>本次响应的条目快照，文件在前、目录在后，最多 200 项以限制事件体积。</summary>
    public FileEntry[] Results { get; init; } = [];
    /// <summary>本次响应条目是否超过 Results 的 200 项上限；不表示服务器是否还有其他分页。</summary>
    public bool ResultsTruncated { get; init; }
}

/// <summary>已添加或删除的自定义视图快照。</summary>
/// <remarks>目前服务器没有为视图提供独立 ID；Name 不是稳定唯一标识。</remarks>
public sealed record PluginViewInfo
{
    /// <summary>视图显示名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>视图类型：0 为搜索筛选，1 为文件夹路径快捷入口。</summary>
    public int Type { get; init; }
    /// <summary>搜索关键字或文件夹绝对路径的快照。</summary>
    public string[] Keywords { get; init; } = [];
    /// <summary>当前界面使用的图标字符。</summary>
    public string Icon { get; init; } = "";
    /// <summary>当前界面使用的颜色字符串。</summary>
    public string Color { get; init; } = "";
}
