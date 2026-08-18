using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Downloader;
using drive_desktop.Services;
using FluentValidation;
using FluentValidation.Results;
using LiveChartsCore.SkiaSharpView.Painting;

namespace drive_desktop.Models;

public class FileOrDirReNameInfo
{
    public int Type { get; set; } //0文件1文件夹
    public string Id { get; set; }
    public string NewName { get; set; }
}

public partial class FileShareData : ObservableValidator
{
    [ObservableProperty] private string _shareFileId;
    [ObservableProperty] private DateTime _beginValidity;
    [ObservableProperty] private DateTime _endValidity;
    [ObservableProperty] public string? password;
    [ObservableProperty] private string? _introduction;
    private FileShareDataValidator val = new();

    /// <summary>
    /// 获取表单验证对象
    /// </summary>
    /// <returns></returns>
    public ValidationResult GetValidationResult()
    {
        return val.Validate(this);
    }

    public FileShareData()
    {
    }

    public FileShareData(Guid shareFileId, DateTime beginValidity, DateTime endValidity, string? password,
        string? introduction)
    {
        BeginValidity = beginValidity;
        EndValidity = endValidity;
        Password = password;
        Introduction = introduction;
    }

    public static implicit operator FileShareDataDTO(FileShareData model)
    {
        return new FileShareDataDTO(model.ShareFileId, model.BeginValidity, model.EndValidity, model.Password,
            model.Introduction);
    }
}

public class FileShareDataDTO
{
    public string ShareFileId { get; set; }

    public DateTime BeginValidity { get; set; }

    public DateTime EndValidity { get; set; }

    public string? Password { get; set; }

    public string? Introduction { get; set; }

    public FileShareDataDTO()
    {
    }

    public FileShareDataDTO(string shareFileId, DateTime beginValidity, DateTime endValidity, string? password,
        string? introduction)
    {
        ShareFileId = shareFileId;
        BeginValidity = beginValidity;
        EndValidity = endValidity;
        Password = password;
        Introduction = introduction;
    }
}

public class FileShareDataValidator : AbstractValidator<FileShareData>
{
    public FileShareDataValidator()
    {
        // 密码规则
        RuleFor(x => x.Password)
            .MaximumLength(10).WithMessage("密码不能超过长度10");

        // 密码规则
        RuleFor(x => x.Introduction)
            .MaximumLength(300).WithMessage("简介长度不能超过300");
        RuleFor(x => x.EndValidity)
            .Must((range, endDate) => endDate > range.BeginValidity)
            .WithMessage("结束日期必须晚于开始日期");
        RuleFor(x => x.BeginValidity)
            .Must((range, beg) => beg.Date >= DateTime.Now.Date)
            .WithMessage("开始日期必须大于当前时间");
    }
}

public class StorageCapacityInfo
{
    /// <summary>
    /// 使用中容量
    /// </summary>
    public double UsedSpaceInBytes { get; set; }

    /// <summary>
    /// 总共容量
    /// </summary>
    public double TotalSpaceInBytes { get; set; }

    /// <summary>
    /// 剩余容量
    /// </summary>
    public double FreeSpaceInBytes
    {
        get
        {
            if (TotalSpaceInBytes < UsedSpaceInBytes)
            {
                return 0;
            }

            return TotalSpaceInBytes - UsedSpaceInBytes;
        }
    }
}

public class UserFilesInfo
{
    public UserFilesInfoItem[] FileInfos { get; set; }
    public UserDirsInfoItem[] Dirs { get; set; }

    public int TotalFileCount { get; set; }
}

public interface UserFiles
{
    public string Id { get; set; }
}

public partial class UserFilesInfoItem : ObservableObject, UserFiles
{
    public string Id { get; set; }

    public string FileName
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public ulong FileSizeInBytes { get; set; }
    public string FileHash { get; set; }
    public string FolderId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastModifiedTime { get; set; }
    public string FileShare { get; set; }
    [ObservableProperty] private bool isChecked = false;
    [ObservableProperty] private string? _imageUrl;

    //内部使用 图标信息
    public FileTypeInfo FileTypeInfo =>
        ConstantResourceService.FileIconHelper.GetFileInfo(FileName);
}

public partial class UserDirsInfoItem : ObservableObject, UserFiles
{
    public string Id { get; set; }
    public string FolderName { get; set; }
    public DateTime CreationTime { get; set; }
    [ObservableProperty] private bool isChecked = false;
}

public class FileOrDirMoveInfo
{
    public string[]? FolderIds { get; set; }
    public string[]? FileIds { get; set; }

    public string NewFolderId { get; set; }
}

public class DashboardStatisticsDto
{
    /// <summary>
    /// 顶部统计摘要卡片数据
    /// </summary>
    public SummaryDataDto SummaryData { get; set; } = new();

    /// <summary>
    /// 文件类型空间分布数据（用于环形图）
    /// </summary>
    public List<FileTypeDataDto> FileTypeData { get; set; } = new();

    /// <summary>
    /// 近期上传趋势活跃度数据（用于平滑折线图）
    /// </summary>
    public List<TrendDataDto> TrendData { get; set; } = new();

    /// <summary>
    /// 大文件空间占用排行数据（用于条形图）
    /// </summary>
    public List<TopFilesDataDto> TopFilesData { get; set; } = new();
}
public sealed class SpaceCategoryData
{
    public required string Name { get; init; }

    public required double[] Values { get; init; }

    public required SolidColorPaint Fill { get; init; }
}
/// <summary>
/// 顶部统计摘要数据
/// </summary>
public class SummaryDataDto
{
    /// <summary>
    /// 用户总存储容量（纯字节 Bytes）
    /// </summary>
    public long TotalSpaceBytes { get; set; }

    /// <summary>
    /// 用户已使用的存储容量（纯字节 Bytes）
    /// </summary>
    public long UsedSpaceBytes { get; set; }

    /// <summary>
    /// 用户网盘内的总文件数量
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// 用户当前活跃（未删除/未过期）的分享链接数量
    /// </summary>
    public int ShareCount { get; set; }
}
public partial class SummaryData : ObservableObject
{
    /// <summary>
    /// 用户总存储容量（纯字节 Bytes）
    /// </summary>
    [ObservableProperty]
    private long _totalSpaceBytes;

    /// <summary>
    /// 用户已使用的存储容量（纯字节 Bytes）
    /// </summary>
    [ObservableProperty]
    private long _usedSpaceBytes;

    /// <summary>
    /// 用户网盘内的总文件数量
    /// </summary>
    [ObservableProperty]
    private int _fileCount;

    /// <summary>
    /// 用户当前活跃（未删除/未过期）的分享链接数量
    /// </summary>
    [ObservableProperty]
    private int _shareCount;
}
/// <summary>
/// 单个文件类型的分布数据
/// </summary>
public class FileTypeDataDto
{
    /// <summary>
    /// 文件类型名称（如：视频 (Video)、图片 (Image) 等）
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 该类型文件占据的总容量大小（纯字节 Bytes）
    /// </summary>
    public long ValueBytes { get; set; }
}
public partial class FileTypeData : ObservableObject
{
    /// <summary>
    /// 文件类型名称（如：视频 (Video)、图片 (Image) 等）
    /// </summary>
    [ObservableProperty]
    private string _name;

    /// <summary>
    /// 该类型文件占据的总容量大小（纯字节 Bytes）
    /// </summary>
    [ObservableProperty]
    private long _valueBytes;
}

/// <summary>
/// 上传趋势统计数据
/// </summary>
public class TrendDataDto
{
    /// <summary>
    /// 统计周期的时间节点列表（如：周一、周二...），用于图表 X 轴
    /// </summary>
    public string Dates { get; set; }

    /// <summary>
    /// 对应时间节点内的上传文件数量列表，用于图表 Y 轴
    /// </summary>
    public int Uploads { get; set; }
}
public partial class TrendData : ObservableObject
{
    /// <summary>
    /// 统计周期的时间节点列表（如：周一、周二...），用于图表 X 轴
    /// </summary>
    [ObservableProperty]
    private string _dates;

    /// <summary>
    /// 对应时间节点内的上传文件数量列表，用于图表 Y 轴
    /// </summary>
    [ObservableProperty]
    private int _uploads;
    
}
/// <summary>
/// 大文件排行统计数据
/// </summary>
public class TopFilesDataDto
{
    /// <summary>
    /// 占用空间最大的文件名称列表，用于图表 Y 轴
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 对应文件的容量大小列表（纯字节 Bytes），用于图表 X 轴
    /// </summary>
    public long SizeByte { get; set; }
}
public partial class TopFilesData : ObservableObject
{
    /// <summary>
    /// 占用空间最大的文件名称列表，用于图表 Y 轴
    /// </summary>
    [ObservableProperty]
    private string _name;

    /// <summary>
    /// 对应文件的容量大小列表（纯字节 Bytes），用于图表 X 轴
    /// </summary>
    [ObservableProperty]
    private long _sizeByte;
    
}
public class BreadcrumbNode(string folderName, string folderId)
{
    public string FolderName { get; set; } = folderName; // 显示的名称，比如 "图片"
    public string FolderId { get; set; } = folderId; // 用来查询后台的真实 ID 或 路径
}

public partial class SearchInfo : ObservableObject
{
    [ObservableProperty] private string _keyword;

    [ObservableProperty] private ObservableCollection<string> _fileType = new ObservableCollection<string>();
    [ObservableProperty] private ulong? _fileSizeInBytesMin;
    [ObservableProperty] private ulong? _fileSizeInBytesMax;
    [ObservableProperty] private DateTime? _starLastModifiedTime;
    [ObservableProperty] private DateTime? _endLastModifiedTime;
    [ObservableProperty] private DateTime? _starCreationTime;
    [ObservableProperty] private DateTime? _endCreationTime;

    /// <summary>
    /// 时间 类型 大小
    /// </summary>
    [ObservableProperty] private ObservableCollection<string> _orderByType = new ObservableCollection<string>();

    public static implicit operator SearchInfoDTO(SearchInfo model)
    {
        return new SearchInfoDTO()
        {
            Keyword = model.Keyword,
            FileType = model.FileType?.ToArray(),
            FileSizeInBytesMax = model.FileSizeInBytesMax * 1024,
            FileSizeInBytesMin = model.FileSizeInBytesMin * 1024,
            EndCreationTime = model.EndCreationTime?.ToString(),
            EndLastModifiedTime = model.EndLastModifiedTime?.ToString(),
            StarCreationTime = model.StarCreationTime?.ToString(),
            StarLastModifiedTime = model.StarLastModifiedTime?.ToString(),
            OrderByType = model.OrderByType?.ToArray()
        };
    }
}

public class SearchInfoDTO
{
    public string? Keyword { get; set; }
    public string[]? FileType { get; set; }

    public ulong? FileSizeInBytesMin { get; set; }
    public ulong? FileSizeInBytesMax { get; set; }
    public string? StarLastModifiedTime { get; set; }
    public string? EndLastModifiedTime { get; set; }
    public string? StarCreationTime { get; set; }
    public string? EndCreationTime { get; set; }

    /// <summary>
    /// 时间 类型 大小
    /// </summary>
    public string[]? OrderByType { get; set; }
}

public class UploadTaskModel
{
    [JsonPropertyName("upload_id")] public Guid UploadId { get; set; }

    [JsonPropertyName("user_id")] public Guid UserId { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    [JsonPropertyName("meta")] public UploadMetaModel Meta { get; set; } = new();

    [JsonPropertyName("uploaded_chunks")] public List<UploadedChunkModel> UploadedChunks { get; set; } = new();

    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
}

public class UploadMetaModel
{
    [JsonPropertyName("file_name")] public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;

    [JsonPropertyName("total_size")] public ulong TotalSize { get; set; }

    [JsonPropertyName("total_chunks")] public int TotalChunks { get; set; }
}

public class UploadedChunkModel
{
    [JsonPropertyName("index")] public int Index { get; set; }

    [JsonPropertyName("size")] public int Size { get; set; }

    [JsonPropertyName("completed_at")] public long CompletedAt { get; set; }
}

// 定义一个结构来同时存储 U码、字体家族 和 颜色
public record FileTypeInfo(
    string IconCode,
    FontFamily FontFamilyResource,
    string HexColor,
    bool IsImg = false,
    string TypeName = "文件");

/// <summary>
/// 初始化分片上传请求的模型。
/// </summary>
/// <param name="FileName"></param>
/// <param name="FileSizeInBytes"></param>
/// <param name="FileHash"></param>
public record MultipartUploadInitRequest(string FileName, ulong FileSizeInBytes, string FileHash);

/// <summary>
/// 文件下载模型
/// </summary>
public partial class FileDownloadInfo : ObservableObject
{
    private DateTime _lastUiUpdateTime = DateTime.MinValue;
    private DateTime _lastDBUpdateTime = DateTime.MinValue;
    public FileTransmissionService fileTransmissionService { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Uid { get; set; }
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _fileId; //文件id
    [ObservableProperty] private int _downloadMode; //0 自己的文件，1 分享文件
    [ObservableProperty] private string? _sharePassword; //分享文件提取密码
    [ObservableProperty] private string _name; //名称
    [ObservableProperty] private string _path; // 下载路径
    [ObservableProperty] private ulong _totalSizeBytes; // 总大小
    [ObservableProperty] private ulong _currentSizeBytes; // 当前大小
    [ObservableProperty] private double _progressPercentage; // 百分比
    [ObservableProperty] private double _bytesPerSecondSpeed; // 当前速度
    [ObservableProperty] private string _time; // 剩余时间
    [ObservableProperty] private string _hash256; // hash
    [ObservableProperty] private bool _isEnd; // 是否下载完成
    [ObservableProperty] private DateTime _endTime; // 结束时间
    [ObservableProperty] private bool _isPause = true; //是否暂停
    [ObservableProperty] private bool _isDel = true; //是否暂停
    public bool IsContinue = false; //用来判断是不是从数据据里读出 用来继续下载
    public DownloadService DownloadService { get; set; } = new DownloadService(FileTransmissionService.downloadOpt);

    public FileDownloadInfo()
    {
        DownloadService.DownloadProgressChanged += OnDownloadProgressChanged;
        DownloadService.DownloadFileCompleted += OnDownloadCompleted;
    }

    public FileTransmissionModel ToFileTransmissionModel()
    {
        return new FileTransmissionModel()
        {
            Id = this.Id,
            Uid = this.Uid,
            Type = 0,
            LocalFilePath = this.Path,
            FileId = this.FileId,
            DownloadMode = this.DownloadMode,
            SharePassword = this.SharePassword,
            FileName = Name,
            TotalSizeBytes = this.TotalSizeBytes,
            CurrentSizeBytes = 0,
            Hash256 = this.Hash256,
            StartTime = DateTime.Now,
            IsDel = false,
            IsEnd = false
        };
        ;
    }

    /// <summary>
    /// FileTransmissionModel to FileDownloadInfo
    /// </summary>
    /// <returns></returns>
    public static FileDownloadInfo FTMToFDI(FileTransmissionModel ftm, string path)
    {
        return new FileDownloadInfo()
        {
            Id = ftm.Id,
            Uid = ftm.Uid,
            IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(ftm.FileName),
            FileId = ftm.FileId,
            DownloadMode = ftm.DownloadMode,
            SharePassword = ftm.SharePassword,
            Name = ftm.FileName,
            TotalSizeBytes = ftm.TotalSizeBytes,
            CurrentSizeBytes = ftm.CurrentSizeBytes,
            Hash256 = ftm.Hash256,
            ProgressPercentage = Math.Round(((double)ftm.CurrentSizeBytes / ftm.TotalSizeBytes), 2) * 100,
            BytesPerSecondSpeed = 0,
            Path = ftm.LocalFilePath,
            IsPause = true,
            IsEnd = false,
            IsContinue = true
        };
        ;
    }

    /// <summary>
    /// 从UserFilesInfoItem转成FileDownloadInfo
    /// </summary>
    /// <returns></returns>
    public static FileDownloadInfo UserFilesInfoItemToFileDownloadInfo(UserFilesInfoItem ufi, string downloadPath,
        Guid userId)
    {
        return new FileDownloadInfo()
        {
            IconInfo = ufi.FileTypeInfo,
            FileId = ufi.Id,
            DownloadMode = 0,
            Uid = userId,
            Name = ufi.FileName,
            TotalSizeBytes = ufi.FileSizeInBytes,
            CurrentSizeBytes = 0,
            Hash256 = ufi.FileHash,
            Path = downloadPath,
            IsEnd = false,
            IsPause = true
        };
        ;
    }

    public static FileDownloadInfo ShareDownloadRequestToFileDownloadInfo(
        ShareDownloadRequest request,
        string downloadPath,
        Guid userId)
    {
        return new FileDownloadInfo
        {
            IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(request.ShareInfo.Name),
            // 分享下载时 FileId 复用为 shareKey，续传时仍用它重新获取临时密钥。
            FileId = request.ShareKey,
            DownloadMode = 1,
            SharePassword = request.Password,
            Uid = userId,
            Name = request.ShareInfo.Name,
            TotalSizeBytes = (ulong)Math.Max(0, request.ShareInfo.SizeInBytes),
            CurrentSizeBytes = 0,
            Hash256 = string.Empty,
            Path = downloadPath,
            IsEnd = false,
            IsPause = true
        };
    }

    /// <summary>
    /// 下载进度回调
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
    {
        //已下载大小
        if ((DateTime.Now - _lastUiUpdateTime).TotalMilliseconds >= 200)
        {
            _lastUiUpdateTime = DateTime.Now;
            CurrentSizeBytes = (ulong)e.ReceivedBytesSize;
        }

        //2秒一次更新进度
        if ((DateTime.Now - _lastDBUpdateTime).TotalMilliseconds >= 2000)
        {
            _lastDBUpdateTime = DateTime.Now;
            _ = fileTransmissionService.DownloadUpdataprogress(Id, CurrentSizeBytes);
        }

        //当前下载速度
        BytesPerSecondSpeed = e.BytesPerSecondSpeed;
        ProgressPercentage = e.ProgressPercentage;
        TimeSpan timeLeft = TimeSpan.Zero;
        if (e.BytesPerSecondSpeed > 0)
        {
            // 剩余字节数 / 每秒下载字节数 = 剩余秒数
            double secondsRemaining = (e.TotalBytesToReceive - e.ReceivedBytesSize) / e.BytesPerSecondSpeed;
            timeLeft = TimeSpan.FromSeconds(secondsRemaining);
        }

        Time = string.Format("{0:hh\\:mm\\:ss}", timeLeft);
    }


    private void OnDownloadCompleted(object sender, AsyncCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            fileTransmissionService.DownloadComplete(this, 2);
        }
        else if (e.Error != null)
        {
            fileTransmissionService.DownloadComplete(this, 1);
        }
        else
        {
            fileTransmissionService.DownloadComplete(this, 0);
        }
    }
}

/// <summary>
/// 文件上传模型
/// </summary>
public partial class FileUploadInfo : ObservableObject
{
    private DateTime _lastUiUpdateTime = DateTime.MinValue;
    private DateTime _lastDBUpdateTime = DateTime.MinValue;
    public FileTransmissionService fileTransmissionService { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Uid { get; set; }
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _name; //名称
    [ObservableProperty] private string _path; // 文件路径
    [ObservableProperty] private string _uploadFolderId; // 上传文件夹Id
    [ObservableProperty] private string _uploadFolderPath; // 上传文件夹路径
    [ObservableProperty] private string _uploadId; // 上传任务Id
    [ObservableProperty] private ulong _totalSizeBytes; // 总大小
    [ObservableProperty] private ulong _currentSizeBytes; // 当前大小
    [ObservableProperty] private double _progressPercentage; // 百分比
    [ObservableProperty] private double _bytesPerSecondSpeed; // 当前速度
    [ObservableProperty] private string _time; // 剩余时间
    [ObservableProperty] private string _hash256; // hash256
    [ObservableProperty] private string _errorMsg; // 错误信息
    [ObservableProperty] private bool _isEnd; // 是否上传完成
    [ObservableProperty] private DateTime _endTime; // 结束时间
    [ObservableProperty] private bool _isPause = true; //是否暂停
    public bool IsContinue = false; //用来判断是不是从数据据里读出 用来继续上传

    public MultipartUploadService UploadService { get; set; } =
        new MultipartUploadService(FileTransmissionService.uploadOpt);

    public FileUploadInfo()
    {
        UploadService.UploadProgressChanged +=
            OnUploadProgressChanged;

        UploadService.UploadFileCompleted +=
            OnUploadCompleted;
    }

    private void OnUploadProgressChanged(
        object? sender,
        UploadProgressChangedEventArgs e)
    {
        //已下载大小
        if ((DateTime.Now - _lastUiUpdateTime).TotalMilliseconds >= 200)
        {
            _lastUiUpdateTime = DateTime.Now;
            CurrentSizeBytes = (ulong)e.UploadedBytesSize;
        }

        //2秒一次更新进度
        if ((DateTime.Now - _lastDBUpdateTime).TotalMilliseconds >= 2000)
        {
            _lastDBUpdateTime = DateTime.Now;
            _ = fileTransmissionService.UploadUpdataprogress(Id, CurrentSizeBytes);
        }

        //当前下载速度
        BytesPerSecondSpeed = e.BytesPerSecondSpeed;
        ProgressPercentage = e.ProgressPercentage;
        TimeSpan timeLeft = TimeSpan.Zero;
        if (e.BytesPerSecondSpeed > 0)
        {
            // 剩余字节数 / 每秒下载字节数 = 剩余秒数
            double secondsRemaining = ((long)TotalSizeBytes - e.UploadedBytesSize) / e.BytesPerSecondSpeed;
            timeLeft = TimeSpan.FromSeconds(secondsRemaining);
        }

        Time = string.Format("{0:hh\\:mm\\:ss}", timeLeft);
    }

    private void OnUploadCompleted(
        object? sender,
        AsyncCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            fileTransmissionService.UploadComplete(this, 2);
        }
        else if (e.Error != null)
        {
            ErrorMsg = e.Error.Message;
            fileTransmissionService.UploadComplete(this, 1);
        }
        else
        {
            fileTransmissionService.UploadComplete(this, 0);
        }
    }

    public FileTransmissionModel ToFileTransmissionModel()
    {
        return new FileTransmissionModel()
        {
            Id = this.Id,
            Uid = this.Uid,
            Type = 1,
            LocalFilePath = Path,
            UploadFolderId = this.UploadFolderId,
            UploadFolderPath = this.UploadFolderPath,
            UploadId = this.UploadId,
            FileName = Name,
            TotalSizeBytes = this.TotalSizeBytes,
            CurrentSizeBytes = 0,
            Hash256 = this.Hash256,
            StartTime = DateTime.Now,
            IsDel = false,
            IsEnd = false
        };
        ;
    }

    /// <summary>
    /// FileTransmissionModel to FileDownloadInfo
    /// </summary>
    /// <returns></returns>
    public static FileUploadInfo FTMToFUI(FileTransmissionModel ftm)
    {
        return new FileUploadInfo()
        {
            Id = ftm.Id,
            Uid = ftm.Uid,
            IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(ftm.FileName),
            Name = ftm.FileName,
            Path = ftm.LocalFilePath,
            TotalSizeBytes = ftm.TotalSizeBytes,
            CurrentSizeBytes = ftm.CurrentSizeBytes,
            Hash256 = ftm.Hash256,
            UploadId = ftm.UploadId,
            UploadFolderId = ftm.UploadFolderId,
            UploadFolderPath = ftm.UploadFolderPath,
            ProgressPercentage = Math.Round(((double)ftm.CurrentSizeBytes / ftm.TotalSizeBytes), 2) * 100,
            BytesPerSecondSpeed = 0,
            IsPause = true,
            IsEnd = false,
            IsContinue = true
        };
        ;
    }
}

/// <summary>
/// 文件传输历史
/// </summary>
public partial class FileHisDownInfo : ObservableObject
{
    public Guid Id { get; set; }
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _name; //名称
    [ObservableProperty] private string _path; // 下载路径
    [ObservableProperty] private ulong _totalSizeBytes; // 总大小
    [ObservableProperty] private DateTime _endTime; // 结束时间
    [ObservableProperty] private string? _reasonFailure; //失败原因
    public bool HasFailure => !string.IsNullOrWhiteSpace(ReasonFailure);
}

/// <summary>
/// 文件传输历史
/// </summary>
public partial class FileHisUpInfo : ObservableObject
{
    public Guid Id { get; set; }
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _name; //名称
    [ObservableProperty] private string _uploadFolderId; // 上传文件夹Id
    [ObservableProperty] private string _uploadFolderPath; // 上传文件夹路径
    [ObservableProperty] private ulong _totalSizeBytes; // 总大小
    [ObservableProperty] private DateTime _endTime; // 结束时间
}

public class ShareInfoPrivateDto
{
    public string Id { get; set; }
    public string ShareFileId { get; set; }
    public string FileName { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime BeginValidity { get; set; }
    public DateTime EndValidity { get; set; }
    public string? Introduction { get; set; }
    public string? Password { get; set; }
}

public partial class ShareInfoPrivateGroup : ObservableObject
{
    [ObservableProperty] private string _shareFileId;
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private ObservableCollection<ShareInfoPrivateItem> _shareInfoPrivateItems = new();
}

public partial class ShareInfoPrivateItem : ObservableObject
{
    
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _shareId;
    [ObservableProperty] private string _shareFileId;
    [ObservableProperty] private DateTime _creationTime;
    [ObservableProperty] private DateTime _beginValidity;
    [ObservableProperty] private DateTime _endValidity;
    [ObservableProperty] private string? _introduction;
    [ObservableProperty] private string? _password;

    /// <summary>
    /// 锁图标
    /// </summary>
    [ObservableProperty] private string? _passwordIcon;

    /// <summary>
    /// 状态  是否过期
    /// </summary>
    [ObservableProperty] private bool _status;

    /// <summary>
    /// 状态图标
    /// </summary>
    [ObservableProperty] private string? _statusIcon;

    public static ShareInfoPrivateItem ToShareInfoPrivateItem(ShareInfoPrivateDto item)
    {
        return new ShareInfoPrivateItem
        {
            FileName = item.FileName,
            IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(item.FileName),
            ShareId = item.Id.ToString(),
            ShareFileId = item.ShareFileId,
            CreationTime = item.CreationTime,
            BeginValidity = item.BeginValidity,
            EndValidity = item.EndValidity,
            Introduction = item.Introduction,
            Password = string.IsNullOrEmpty(item.Password) ? "无" : item.Password,
            PasswordIcon = item.Password == null ? "\uf09c" : "\uf023",
            Status = item.EndValidity < DateTime.Now,
            StatusIcon = item.EndValidity < DateTime.Now ? "\uf06a" : "\uf058",
        };
    }
    
    
}

public class FileShareInfoDto
{
    /// <summary>用户ID</summary>
    public string UserId { get; set; }

    /// <summary>昵称</summary>
    public string Nickname { get; set; }

    /// <summary>头像URL</summary>
    public string AvatarUrl { get; set; }

    /// <summary>分享文件ID</summary>
    public string ShareFileId { get; set; }

    /// <summary>有效期开始时间</summary>
    public DateTime BeginValidity { get; set; }

    /// <summary>有效期结束时间</summary>
    public DateTime EndValidity { get; set; }

    /// <summary>简介（可为空）</summary>
    public string Introduction { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>是否有密码</summary>
    public bool IsPassword { get; set; }

    /// <summary>文件大小（字节）</summary>
    public long SizeInBytes { get; set; }

    /// <summary>文件名</summary>
    public string Name { get; set; }
}


public partial class FileSearchSimpleItem : ObservableObject
{

    public string Id { get; set; }
    [ObservableProperty] private FileTypeInfo _iconInfo; //图标信息
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _path;
}
