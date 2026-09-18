using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using drive_desktop.Models;
using drive_desktop.Services.Plugins;
using Drive.Plugin.Abi;
using Drive.Plugin.SDK;

namespace drive_desktop.Services;

public partial class FileTransmissionService
{
    // Return after persistence/queue acceptance, not after the complete transfer.
    internal async Task<Guid> EnqueuePluginDownloadAsync(UserFilesInfoItem file, Guid userId, CancellationToken cancellationToken)
    {
        await InitializeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (_userInfoService.ShowUserInfo.UserId != userId) throw new PluginException(PluginError.Cancelled, "账户已改变。");
        var item = FileDownloadInfo.UserFilesInfoItemToFileDownloadInfo(file, _appConfigService.Config.DownloadLocation, userId);
        var accepted = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ObservePluginDownloadAsync(item, accepted, cancellationToken);
        return await accepted.Task.WaitAsync(cancellationToken);
    }

    private async Task ObservePluginDownloadAsync(FileDownloadInfo item, TaskCompletionSource<Guid> accepted, CancellationToken cancellationToken)
    {
        try { await AddDownloadCore(item, accepted, cancellationToken); }
        catch (Exception error)
        {
            accepted.TrySetException(error);
            SetPauseState(item, true); item.IsContinue = true; ReleaseDownloadSlot(item);
            PluginEventHub.Publish(new() { Id = DriveEventId.DownloadFailed, Transfer = PluginDtoMapper.Download(item, TransferState.Failed, error.Message) });
        }
    }

    internal async Task<Guid> EnqueuePluginUploadAsync(string path, string folderId, Guid userId, CancellationToken cancellationToken)
    {
        await InitializeAsync();
        // Hashing must not block Avalonia's dispatcher. Existing hashing streams asynchronously.
        var hash = await ConstantResourceService.ComputeFileHashAsync(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (_userInfoService.ShowUserInfo.UserId != userId) throw new PluginException(PluginError.Cancelled, "账户已改变。");
        var name = Path.GetFileName(path);
        var item = new FileUploadInfo
        {
            Uid = userId, Name = name, Path = path, UploadFolderId = folderId, UploadFolderPath = "",
            IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(name), TotalSizeBytes = (ulong)new FileInfo(path).Length,
            CurrentSizeBytes = 0, Hash256 = hash, IsEnd = false, IsPause = false
        };
        var accepted = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ObservePluginUploadAsync(item, accepted, cancellationToken);
        return await accepted.Task.WaitAsync(cancellationToken);
    }

    private async Task ObservePluginUploadAsync(FileUploadInfo item, TaskCompletionSource<Guid> accepted, CancellationToken cancellationToken)
    {
        try { await AddUpload(item, accepted, cancellationToken); }
        catch (Exception error)
        {
            accepted.TrySetException(error);
            SetPauseState(item, true); item.IsContinue = true; ReleaseUploadSlot(item);
            PluginEventHub.Publish(new() { Id = DriveEventId.UploadFailed, FolderId = item.UploadFolderId, Transfer = PluginDtoMapper.Upload(item, TransferState.Failed, error.Message) });
        }
    }
}
