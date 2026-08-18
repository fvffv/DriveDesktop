using System;
using SQLite;

namespace drive_desktop.Models;

/// <summary>
/// 建表专用 建立db文件传输库
/// </summary>
public class FileTransmissionModel
{
    [PrimaryKey]
    public Guid Id { get; set; }
    /// <summary>
    /// 用户id
    /// </summary>
    [Indexed(Name = "TypeEndIndex", Order = 1)]
    [Indexed(Name = "ActiveTaskIndex", Order = 1)]
    public Guid Uid { get; set; }
    /// <summary>
    /// 类型 0 上传  1 下载
    /// </summary>
    [Indexed(Name = "TypeEndIndex", Order = 1)]
    public int Type { get; set; }

    /// <summary>
    /// 文件id
    /// </summary>
    [Indexed]
    public string FileId { get; set; }

    /// <summary>
    /// 下载来源：0 自己的文件，1 分享文件。
    /// </summary>
    public int DownloadMode { get; set; }

    /// <summary>
    /// 分享文件的提取密码，用于应用重启后的断点续传。
    /// </summary>
    public string? SharePassword { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// 上传到的文件夹id
    /// </summary>
    public string UploadFolderId { get; set; }
    /// <summary>
    /// 上传到的文件夹路径
    /// </summary>
    public string UploadFolderPath { get; set; }
    /// <summary>
    /// 上传文件在本地的完整路径。 下载的路径
    /// </summary>
    public string LocalFilePath { get; set; } 
    /// <summary>
    /// 文件上传任务id
    /// </summary>
    public string UploadId { get; set; }

    /// <summary>
    /// 文件总大小 
    /// </summary>
    public ulong TotalSizeBytes { get; set; }

    /// <summary>
    /// 已经上传或下载的大小
    /// </summary>
    public ulong CurrentSizeBytes { get; set; }

    /// <summary>
    /// Hash256
    /// </summary>
    public string Hash256 { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    [Indexed]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 是否完成
    /// </summary>
    // 复合索引的第二列：配合 Type 快速筛出需要继续排队的任务
    [Indexed(Name = "TypeEndIndex", Order = 2)]
    public bool IsEnd { get; set; }
    /// <summary>
    /// 是否删除记录
    /// </summary>
    [Indexed(Name = "ActiveTaskIndex", Order = 1)]
    public bool IsDel { get; set; }
    /// <summary>
    /// 失败原因
    /// </summary>
    public string ReasonFailure { get; set; }
}
