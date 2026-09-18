using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using drive_desktop.Models;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;
using Refit;

namespace drive_desktop.Services.Plugins;

// One typed wrapper observes all callers, including existing UI flows. No runtime proxy/reflection.
internal sealed class PluginFileApiDecorator(ICloudDriveFileApi inner) : ICloudDriveFileApi
{
    private volatile ConcurrentDictionary<string, ShareLinkInfo> _knownShares = new(StringComparer.Ordinal);

    /// <summary>账户切换时丢弃分享关联，并使旧账户尚未完成的分享、搜索通知失效。</summary>
    internal void ResetAccount()
    {
        _knownShares = new(StringComparer.Ordinal);
    }
    private static async Task<DefaultMsg> ObserveAsync(Task<DefaultMsg> operation, Func<DefaultMsg, DriveEvent> makeEvent)
    {
        var result = await operation.ConfigureAwait(false);
        if (result.Status == 0) PluginEventHub.Publish(makeEvent(result));
        return result;
    }
    public Task<DefaultMsg> UploadFileAsync(StreamPart file, string folderId) => ObserveAsync(inner.UploadFileAsync(file, folderId), _ => new() { Id = DriveEventId.FileCreated, FolderId = folderId });
    public Task<DefaultMsg<StorageCapacityInfo>> GetUserStorageCapacityInfoAsync() => inner.GetUserStorageCapacityInfoAsync();
    public Task<DefaultMsg> SaveToFileAsync(string folderId, string fileId, string hash256) => ObserveAsync(inner.SaveToFileAsync(folderId, fileId, hash256), _ => new() { Id = string.IsNullOrEmpty(fileId) ? DriveEventId.FileCreated : DriveEventId.FileCopied, FileIds = string.IsNullOrEmpty(fileId) ? [] : [fileId], FolderId = folderId });
    public Task<DefaultMsg> GetFolderRootAsync() => inner.GetFolderRootAsync();
    public Task<DefaultMsg<CloudInfo>> GetCloudInfoAsync() => inner.GetCloudInfoAsync();
    public Task<DefaultMsg<UserFilesInfo>> GetUserDirectoryFileInfoAsync(string folderId, int pageIndex = 1, int pageSize = 50) => inner.GetUserDirectoryFileInfoAsync(folderId, pageIndex, pageSize);
    /// <summary>新建成功后发送独立目录事件，并保留原有创建通知。</summary>
    public async Task<DefaultMsg> CreateFolderAsync(string folderId, string name)
    {
        var result = await inner.CreateFolderAsync(folderId, name).ConfigureAwait(false);
        if (result.Status == 0)
        {
            var id = result.Data?.ToString();
            string[] ids = string.IsNullOrWhiteSpace(id) ? [] : [id];
            PluginEventHub.Publish(new() { Id = DriveEventId.FolderCreated, FolderId = folderId, FolderIds = ids, Name = name });
            PluginEventHub.Publish(new() { Id = DriveEventId.FileCreated, FolderId = folderId, FolderIds = ids, Name = name });
        }
        return result;
    }
    public Task<DefaultMsg> GetFolderByPathStrictAsync(string rootPathId, string fullPath) => inner.GetFolderByPathStrictAsync(rootPathId, fullPath);
    /// <summary>复制请求中的文件 ID，成功后报告这批删除结果。</summary>
    public Task<DefaultMsg> DeleteUserFileAsync(string[] fileIds)
    {
        var ids = fileIds.ToArray();
        return ObserveAsync(inner.DeleteUserFileAsync(ids), _ => new() { Id = DriveEventId.FilesDeleted, FileIds = ids });
    }
    /// <summary>复制请求中的目录 ID，成功后报告这批删除结果。</summary>
    public Task<DefaultMsg> DeleteUserFolderAsync(string[] folderIds)
    {
        var ids = folderIds.ToArray();
        return ObserveAsync(inner.DeleteUserFolderAsync(ids), _ => new() { Id = DriveEventId.FilesDeleted, FolderIds = ids });
    }
    /// <summary>固定重命名目标和新名称，成功后区分文件与目录通知。</summary>
    public Task<DefaultMsg> RenameFileOrDirAsync(FileOrDirReNameInfo info)
    {
        var request = new FileOrDirReNameInfo { Id = info.Id, Type = info.Type, NewName = info.NewName };
        return ObserveAsync(inner.RenameFileOrDirAsync(request), _ => new() { Id = DriveEventId.FileRenamed,
            FileIds = request.Type == 0 ? [request.Id] : [], FolderIds = request.Type == 1 ? [request.Id] : [], Name = request.NewName });
    }
    public Task<DefaultMsg> GetFileDownLoadTempKeyAsync(string fileId) => inner.GetFileDownLoadTempKeyAsync(fileId);
    public Task<DefaultMsg> GetShareTempDownLoadKeyAsync(string shareKey, string pwd=null) => inner.GetShareTempDownLoadKeyAsync(shareKey, pwd);
    public Task<DefaultMsg> MoveFileOrDirAsync(FileOrDirMoveInfo info) => ObserveAsync(inner.MoveFileOrDirAsync(info), _ => new() { Id = DriveEventId.FilesMoved, FileIds = info.FileIds?.ToArray() ?? [], FolderIds = info.FolderIds?.ToArray() ?? [], FolderId = info.NewFolderId });
    /// <summary>成功创建分享后通知插件，复制设置但不传递密码。</summary>
    public async Task<DefaultMsg> CreateShareKeyAsync(FileShareDataDTO info)
    {
        var account = _knownShares;
        var snapshot = ShareSnapshot(info);
        var result = await inner.CreateShareKeyAsync(info).ConfigureAwait(false);
        if (result.Status == 0 && ReferenceEquals(account, _knownShares))
            PublishShare(DriveEventId.ShareLinkCreated, snapshot with { ShareKey = result.Data?.ToString() ?? "" });
        return result;
    }
    /// <summary>分享更新或删除成功后通知；删除文件身份取自当前客户端已读取的分享列表。</summary>
    public async Task<DefaultMsg> UpdateShareFileInfoAsync(string shareId, bool isDel, FileShareDataDTO? fileShareData = null)
    {
        var cache = _knownShares;
        cache.TryGetValue(shareId, out var known);
        var snapshot = (fileShareData is null ? known ?? new ShareLinkInfo() : ShareSnapshot(fileShareData)) with { ShareId = shareId };
        var result = await inner.UpdateShareFileInfoAsync(shareId, isDel, fileShareData).ConfigureAwait(false);
        if (result.Status == 0 && ReferenceEquals(cache, _knownShares))
        {
            if (isDel) cache.TryRemove(shareId, out _);
            else cache[shareId] = snapshot;
            PublishShare(isDel ? DriveEventId.ShareLinkDeleted : DriveEventId.ShareLinkUpdated, snapshot);
        }
        return result;
    }
    public Task<DefaultMsg<FileShareInfoDto>> GetShareInfoAsync(string shareKey) => inner.GetShareInfoAsync(shareKey);
    /// <summary>普通与语义搜索共享入口，成功后发送条件及有限大小的结果快照。</summary>
    public async Task<DefaultMsg<UserFilesInfo>> SearchFilesAsync(SearchInfoDTO searchInfoDto, bool isAI = true)
    {
        var account = _knownShares;
        var snapshot = new FileSearchInfo
        {
            Keyword = searchInfoDto.Keyword ?? "", IsSemantic = isAI, FileTypes = searchInfoDto.FileType?.ToArray() ?? [],
            MinSize = searchInfoDto.FileSizeInBytesMin, MaxSize = searchInfoDto.FileSizeInBytesMax,
            ModifiedAfter = searchInfoDto.StarLastModifiedTime ?? "", ModifiedBefore = searchInfoDto.EndLastModifiedTime ?? "",
            CreatedAfter = searchInfoDto.StarCreationTime ?? "", CreatedBefore = searchInfoDto.EndCreationTime ?? "",
            OrderBy = searchInfoDto.OrderByType?.ToArray() ?? []
        };
        var result = await inner.SearchFilesAsync(searchInfoDto, isAI).ConfigureAwait(false);
        if (result.Status == 0 && ReferenceEquals(account, _knownShares))
        {
            var files = result.Data?.FileInfos ?? [];
            var folders = result.Data?.Dirs ?? [];
            var entries = files.Take(200).Select(PluginDtoMapper.File)
                .Concat(folders.Take(Math.Max(0, 200 - files.Length)).Select(x => PluginDtoMapper.Folder(x))).ToArray();
            PluginEventHub.Publish(new() { Id = DriveEventId.FileSearched, Search = snapshot with
            {
                TotalFiles = result.Data?.TotalFileCount ?? 0, ReturnedFileCount = files.Length, ReturnedFolderCount = folders.Length,
                Results = entries, ResultsTruncated = (long)files.Length + folders.Length > entries.Length
            }});
        }
        return result;
    }
    /// <summary>读取分享列表时记录不含密码的快照，供后续删除通知关联文件。</summary>
    public async Task<DefaultMsg<ShareInfoPrivateDto[]>> GetShareFilesInfoPrivateAsync()
    {
        var cache = _knownShares;
        var result = await inner.GetShareFilesInfoPrivateAsync().ConfigureAwait(false);
        if (result.Status == 0)
        {
            cache.Clear();
            foreach (var item in result.Data ?? [])
                cache[item.Id] = new() { ShareId = item.Id, FileId = item.ShareFileId, BeginValidity = item.BeginValidity,
                    EndValidity = item.EndValidity, Introduction = item.Introduction ?? "", HasPassword = !string.IsNullOrEmpty(item.Password) };
        }
        return result;
    }

    /// <summary>创建不含访问密码的分享事件数据。</summary>
    private static ShareLinkInfo ShareSnapshot(FileShareDataDTO info)
    {
        return new() { FileId = info.ShareFileId ?? "", BeginValidity = info.BeginValidity, EndValidity = info.EndValidity,
            Introduction = info.Introduction ?? "", HasPassword = !string.IsNullOrEmpty(info.Password) };
    }

    /// <summary>发布已确认成功的分享链接变动。</summary>
    private static void PublishShare(DriveEventId id, ShareLinkInfo snapshot)
    {
        PluginEventHub.Publish(new() { Id = id, FileIds = string.IsNullOrEmpty(snapshot.FileId) ? [] : [snapshot.FileId], ShareLink = snapshot });
    }
    public Task<DefaultMsg<DashboardStatisticsDto>> GetDataStatisticsAsync() => inner.GetDataStatisticsAsync();
    public Task<DefaultMsg> GetFullFolderPathAsync(string folderId) => inner.GetFullFolderPathAsync(folderId);
    public Task<DefaultMsg> GetUserFileInfo(string fileId) => inner.GetUserFileInfo(fileId);
    public Task<Stream> DownLoadKey(string key) => inner.DownLoadKey(key);
    public Task<DefaultMsg> CreateMultipartUpload(MultipartUploadInitRequest req) => inner.CreateMultipartUpload(req);
    public Task<DefaultMsg<UploadTaskModel>> GetFileChunkInfo(string uploadId) => inner.GetFileChunkInfo(uploadId);
    public Task<DefaultMsg> MergeFiles(string uploadId, string folderId) => ObserveAsync(inner.MergeFiles(uploadId, folderId), _ => new() { Id = DriveEventId.FileCreated, FolderId = folderId });
}
