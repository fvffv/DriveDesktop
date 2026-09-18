using drive_desktop.Models;
using Drive.Plugin.SDK;

namespace drive_desktop.Services.Plugins;

internal static class PluginDtoMapper
{
    /// <summary>复制自定义视图设置，避免异步保存后界面继续编辑影响事件载荷。</summary>
    public static PluginViewInfo View(CustomView item)
    {
        return new() { Name = item.Name ?? "", Type = item.Type, Keywords = item.Keywords is null ? [] : [.. item.Keywords],
            Icon = item.Icon ?? "", Color = item.Color ?? "" };
    }
    public static FileEntry File(UserFilesInfoItem item) => new()
    {
        Id = item.Id, Name = item.FileName, FolderId = item.FolderId ?? "",
        Size = item.FileSizeInBytes, Hash = item.FileHash ?? "", CreatedAt = item.CreationTime, ModifiedAt = item.LastModifiedTime
    };
    public static FileEntry Folder(UserDirsInfoItem item, string parent = "") => new()
    { Id = item.Id, Name = item.FolderName, IsFolder = true, FolderId = parent, CreatedAt = item.CreationTime };
    public static TransferInfo Download(FileDownloadInfo item, TransferState? state = null, string? error = null) => new()
    {
        Id = item.Id.ToString(), FileId = item.FileId ?? "", Name = item.Name,
        TotalBytes = item.TotalSizeBytes, TransferredBytes = item.CurrentSizeBytes, BytesPerSecond = item.BytesPerSecondSpeed,
        State = state ?? (item.IsEnd ? TransferState.Completed : item.IsPause ? TransferState.Paused : item.DownloadService.IsBusy ? TransferState.Running : TransferState.Queued), Error = error
    };
    public static TransferInfo Upload(FileUploadInfo item, TransferState? state = null, string? error = null) => new()
    {
        Id = item.Id.ToString(), Name = item.Name, FolderId = item.UploadFolderId ?? "",
        TotalBytes = item.TotalSizeBytes, TransferredBytes = item.CurrentSizeBytes, BytesPerSecond = item.BytesPerSecondSpeed,
        State = state ?? (item.IsEnd ? TransferState.Completed : item.IsPause ? TransferState.Paused : item.UploadService.IsRunning ? TransferState.Running : TransferState.Queued), Error = error
    };
}
