using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Downloader;
using drive_desktop.Models;
using drive_desktop.ViewModels;
using drive_desktop.Views;
using SQLite;
using Ursa.Controls;


namespace drive_desktop.Services;

public partial class FileTransmissionService : ObservableObject
{
    private readonly object _taskStatisticsLock = new();

    /// <summary>
    /// 本轮统计中的全部任务 ID。
    /// </summary>
    private readonly HashSet<Guid> _trackedTaskIds = new();

    /// <summary>
    /// 已经成功完成的任务 ID。
    /// </summary>
    private readonly HashSet<Guid> _completedTaskIds = new();

    /// <summary>
    /// 当前处于暂停状态的上传、下载任务 ID。
    /// </summary>
    private readonly HashSet<Guid> _pausedTaskIds = new();

    /// <summary>
    /// 本轮总任务数。
    /// 添加任务时增加，取消任务时减少，成功完成时不减少。
    /// </summary>
    [NotifyPropertyChangedFor(nameof(RemainingTsTaskNum))] [ObservableProperty]
    private int _totalTsTaskNum;
    /// <summary>
    /// 本轮总暂停数。
    /// </summary>
    [NotifyPropertyChangedFor(nameof(RemainingTsTaskNum))] [ObservableProperty]
    private int _totalPauseNum;
    /// <summary>
    /// 已成功完成任务数。
    /// </summary>
    [NotifyPropertyChangedFor(nameof(RemainingTsTaskNum))] [ObservableProperty]
    private int _overTsTaskNum;
    /// <summary>
    /// 今日完成数。
    /// </summary>
    [NotifyPropertyChangedFor(nameof(RemainingTsTaskNum))] [ObservableProperty]
    private int _overTsTaskDayNum;
    /// <summary>
    /// 剩余任务数。
    /// </summary>
    public int RemainingTsTaskNum =>
        Math.Max(0, TotalTsTaskNum - OverTsTaskNum);

    private int pageSkip = 0;
    public bool IsConditionMetDownload => !FileDownloadInfos.Any();
    public bool IsConditionMetUpload => !FileUploadInfos.Any();
    public bool IsConditionMetHis => !FileHistoryInfos.Any();

    public static DownloadConfiguration downloadOpt = new DownloadConfiguration()
    {
        EnableAutoResumeDownload = true,
        ChunkCount = 8, // 分片数量（多线程下载，提升速度）
        ParallelDownload = true,
        MaxTryAgainOnFailure = 3, // 失败重试次数
        HttpClientTimeout = 10000,
        ClearPackageOnCompletionWithFailure = false // 失败时不删除临时文件，用于断点续传
    };

    public static UploadConfiguration uploadOpt;

    /// <summary>
    /// 下载同时删除的数
    /// </summary>
    private readonly SemaphoreSlim _RemDownSemaphore = new SemaphoreSlim(5, 5);

    /// <summary>
    /// 上传同时删除的数
    /// </summary>
    private readonly SemaphoreSlim _RemUpSemaphore = new SemaphoreSlim(5, 5);

    /// <summary>
    /// 同时下载的数量
    /// </summary>
    private readonly SemaphoreSlim _downloadSemaphore;

    private readonly ConcurrentDictionary<Guid, bool> _activeDownloads = new();

    /// <summary>
    /// 同时上传的数量
    /// </summary>
    private readonly SemaphoreSlim _uploadSemaphore;

    private readonly ConcurrentDictionary<Guid, bool> _activeUploads = new();

    /// <summary>
    /// 文件下载列表
    /// </summary>
    [NotifyPropertyChangedFor(nameof(IsConditionMetDownload))] [ObservableProperty]
    private ObservableCollection<FileDownloadInfo> _fileDownloadInfos = new();

    /// <summary>
    /// 文件上传列表
    /// </summary>
    [NotifyPropertyChangedFor(nameof(IsConditionMetUpload))] [ObservableProperty]
    private ObservableCollection<FileUploadInfo> _fileUploadInfos = new();

    /// <summary>
    /// 文件传输历史
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsConditionMetHis))]
    private ObservableCollection<Object> _fileHistoryInfos = new();

    private readonly string _dbPath = Path.Combine(AppContext.BaseDirectory, "data.db");
    public static SQLiteAsyncConnection _db;
    private readonly AppConfigService _appConfigService;
    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly FilePageViewModel _filePageViewModel;

    public FileTransmissionService(UserInfoService userInfoService, FilePageViewModel filePageViewModel,
        WebApiService webApiService,
        AppConfigService appConfigService)
    {
        _userInfoService = userInfoService;
        _filePageViewModel = filePageViewModel;
        _webApiService = webApiService;
        _appConfigService = appConfigService;
        _downloadSemaphore = new SemaphoreSlim(_appConfigService.Config.DownloadSemaphore,
            _appConfigService.Config.DownloadSemaphore);
        _uploadSemaphore = new SemaphoreSlim(_appConfigService.Config.UploadSemaphore,
            _appConfigService.Config.UploadSemaphore);

        uploadOpt = new UploadConfiguration
        {
            ApiUrl = $"{_appConfigService.Config.ServerIp}/api/Files/UploadChunk",
            JWT = $"Bearer {_appConfigService.Config.JWT}",
            FileChunkSizeBytes = _userInfoService.CloudInfo.FileChunkSizeBytes,
            MaxConcurrentChunks = 8,
            HttpClientTimeout = 99990_000
        };


        //创建数据库连接
        CreateDB();
        LoadDataAsync();
        WeakReferenceMessenger.Default.Register<FileTmMessage>(this,
            async (recipient, message) =>
            {
                if (message.Type == 0)
                {
                    if (message.mode == 1 && message.Item is ShareDownloadRequest shareRequest)
                    {
                        await AddShareDownload(shareRequest);
                    }
                    else if (message.Item is UserFilesInfoItem file)
                    {
                        await AddDownload(file);
                    }
                }

                //批量下载
                if (message.Type == 2)
                {
                    _filePageViewModel.FileIsAllChecked = false;
                    _filePageViewModel.FolderIsAllChecked = false;
                    var t = _filePageViewModel.FileInfos.Where(x => x.IsChecked).ToArray();

                    foreach (var item in t)
                    {
                        item.IsChecked = false;
                        AddDownload(item);
                    }
                }

                //上传
                if (message.Type == 1)
                {
                    try
                    {
                        List<string> files = message.Item as List<string>;
                        foreach (var item in files)
                        {
                            //获取基本信息
                            string FileName = Path.GetFileName(item);
                            string hash = await ConstantResourceService.ComputeFileHashAsync(item);

                            var fui = new FileUploadInfo()
                            {
                                Uid = _userInfoService.ShowUserInfo.UserId,
                                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(FileName),
                                Name = FileName,
                                Path = item,
                                UploadFolderId = message.folderId,
                                UploadFolderPath = message.folderPath,
                                CurrentSizeBytes = 0,
                                TotalSizeBytes = (ulong)new FileInfo(item).Length,
                                Hash256 = hash,
                                IsEnd = false,
                                IsPause = false
                            };
                            AddUpload(fui);
                        }
                    }
                    catch (Exception e)
                    {
                        Home.GlobalToastManager?.Show(
                            new Toast($"错误:{e.Message}"),
                            type: NotificationType.Error);
                    }
                   
                }
            });
    }

    public FileTransmissionService()
    {
    }

    public async Task LoadDataAsync()
    {
        //筛选出未完成未删除的数据
        var tsData = await _db.Table<FileTransmissionModel>()
            .Where(x => x.Uid == _userInfoService.ShowUserInfo.UserId && x.IsDel == false).ToArrayAsync();
        var download = tsData
            .Where(x => x.Type == 0 && x.IsEnd == false).Select(x =>
                FileDownloadInfo.FTMToFDI(x, _appConfigService.Config.DownloadLocation));
        var upload = tsData
            .Where(x => x.Type == 1 && x.IsEnd == false).Select(x =>
                FileUploadInfo.FTMToFUI(x));
        foreach (var item in download)
        {
            FileDownloadInfos.Add(item);
            TrackTransferTask(item.Id, item.IsPause);
        }

        foreach (var item in upload)
        {
            FileUploadInfos.Add(item);
            TrackTransferTask(item.Id, item.IsPause);
        }
        await LoadNextTsPage();
        await GetDayOverTaskNum();
    }

    /// <summary>
    /// 创建数据库
    /// </summary>
    public async Task CreateDB()
    {
        _db = new SQLiteAsyncConnection(_dbPath);
        await _db.CreateTableAsync<FileTransmissionModel>();
    }

    /// <summary>
    /// 添加下载任务
    /// </summary>
    /// <param name="ufi"></param>
    public Task AddDownload(UserFilesInfoItem ufi)
    {
        var item = FileDownloadInfo.UserFilesInfoItemToFileDownloadInfo(
            ufi,
            _appConfigService.Config.DownloadLocation,
            _userInfoService.ShowUserInfo.UserId);
        return AddDownloadCore(item);
    }

    private Task AddShareDownload(ShareDownloadRequest request)
    {
        var item = FileDownloadInfo.ShareDownloadRequestToFileDownloadInfo(
            request,
            _appConfigService.Config.DownloadLocation,
            _userInfoService.ShowUserInfo.UserId);
        return AddDownloadCore(item);
    }

    private async Task AddDownloadCore(FileDownloadInfo item)
    {
        //检测是否重复下载
        if (FileDownloadInfos.Any(x =>
                x.DownloadMode == item.DownloadMode &&
                x.FileId == item.FileId))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {item.Name} 下载任务已存在"),
                type: NotificationType.Warning);
            return;
        }

        Home.GlobalToastManager?.Show(
            new Toast($"文件 {item.Name} 已添加至下载列表"),
            type: NotificationType.Success
        );


        if (string.IsNullOrWhiteSpace(_appConfigService.Config.DownloadLocation))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"下载目录设置有误,将使用默认下载路径"),
                type: NotificationType.Warning);
            _appConfigService.Config.DownloadLocation =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
        }

        if (!Directory.Exists(_appConfigService.Config.DownloadLocation))
        {
            Directory.CreateDirectory(_appConfigService.Config.DownloadLocation);
        }

        item.Path = _appConfigService.Config.DownloadLocation;
        //让FileDownloadInfo内部能调用FileTransmissionService的DownloadComplete 有点破坏设计模式了。。。。。
        item.fileTransmissionService = this;
        item.IsPause = false;
        //加入下载列表
        FileDownloadInfos.Add(item);
        TrackTransferTask(item.Id, item.IsPause);
        OnPropertyChanged(nameof(IsConditionMetDownload));
        //入库
        await _db.InsertAsync(item.ToFileTransmissionModel(), typeof(FileTransmissionModel));
        await _downloadSemaphore.WaitAsync();
        // 排到队发现被暂停  则返回 并且加入断点续传
        if (item.IsPause)
        {
            _downloadSemaphore.Release();
            item.IsContinue = true; // 标记为需要断点续传，方便后续恢复
            return;
        }

        // 从请求临时密钥开始就登记为活动任务，暂停时才能正确归还并发名额。
        _activeDownloads.TryAdd(item.Id, true);

        DefaultMsg tmpkey;
        try
        {
            tmpkey = await GetDownloadTempKeyAsync(item);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {item.Name} 获取临时下载密钥失败:{exception.Message}"),
                type: NotificationType.Error);
            SetPauseState(item, true);
            item.IsContinue = true;
            ReleaseDownloadSlot(item);
            return;
        }

        if (item.IsPause)
        {
            item.IsContinue = true;
            ReleaseDownloadSlot(item);
            return;
        }

        if (tmpkey.Status != 0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取临时下载密钥失败:{tmpkey.Msg}"),
                type: NotificationType.Error);
            ReleaseDownloadSlot(item);
            await MoveDownloadToFailureHistoryAsync(item, tmpkey.Msg);
            return;
        }

        //开始下载
        await item.DownloadService.DownloadFileTaskAsync(
            _appConfigService.Config.ServerIp + $"/api/Files/DownLoadKey/{tmpkey.Data}",
            Path.Combine(item.Path, item.Name));
    }

    private Task<DefaultMsg> GetDownloadTempKeyAsync(FileDownloadInfo item)
    {
        return item.DownloadMode == 1
            ? _webApiService.FileApi.GetShareTempDownLoadKeyAsync(item.FileId, item.SharePassword)
            : _webApiService.FileApi.GetFileDownLoadTempKeyAsync(item.FileId);
    }

    private void ReleaseDownloadSlot(FileDownloadInfo item)
    {
        if (_activeDownloads.TryRemove(item.Id, out _))
            _downloadSemaphore.Release();
    }

    private async Task MoveDownloadToFailureHistoryAsync(
        FileDownloadInfo download,
        string? reason)
    {
        string failureReason = string.IsNullOrWhiteSpace(reason)
            ? "获取临时下载密钥失败"
            : reason;

        var dbItem = await _db.FindAsync<FileTransmissionModel>(download.Id);
        if (dbItem is null)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {download.Name} 的本地下载记录不存在"),
                type: NotificationType.Error);
            return;
        }

        dbItem.IsEnd = true;
        dbItem.IsDel = false;
        dbItem.EndTime = DateTime.Now;
        dbItem.CurrentSizeBytes = download.CurrentSizeBytes;
        dbItem.ReasonFailure = failureReason;
        await _db.UpdateAsync(dbItem, typeof(FileTransmissionModel));

        FileDownloadInfos.Remove(download);
        RemoveTransferTask(download.Id);
        OnPropertyChanged(nameof(IsConditionMetDownload));

        if (FileHistoryInfos.OfType<FileHisDownInfo>().All(x => x.Id != download.Id))
        {
            FileHistoryInfos.Insert(0, new FileHisDownInfo
            {
                Id = download.Id,
                EndTime = dbItem.EndTime,
                IconInfo = download.IconInfo,
                Name = download.Name,
                Path = download.Path,
                TotalSizeBytes = download.TotalSizeBytes,
                ReasonFailure = failureReason
            });
            pageSkip++;
        }

        OnPropertyChanged(nameof(IsConditionMetHis));
    }

    /// <summary>
    /// 中断继续下载
    /// </summary>
    /// <param name="fdi"></param>
    private async Task DownloadInterruptResume(FileDownloadInfo fdi)
    {
        //检测是否重复下载

        if (string.IsNullOrWhiteSpace(_appConfigService.Config.DownloadLocation))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"下载目录设置有误,将使用默认下载路径"),
                type: NotificationType.Warning);
            _appConfigService.Config.DownloadLocation =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
        }

        if (!Directory.Exists(_appConfigService.Config.DownloadLocation))
        {
            Directory.CreateDirectory(_appConfigService.Config.DownloadLocation);
        }

        await _downloadSemaphore.WaitAsync();
        //排到队发现被暂停  则返回 并且加入断点续传
        if (fdi.IsPause)
        {
            _downloadSemaphore.Release();
            fdi.IsContinue = true;
            return;
        }


        _activeDownloads.TryAdd(fdi.Id, true);

        DefaultMsg tmpkey;
        try
        {
            tmpkey = await GetDownloadTempKeyAsync(fdi);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Home.GlobalToastManager?.Show(
                new Toast($"文件:{fdi.Name} 获取临时下载密钥失败:{exception.Message}"),
                type: NotificationType.Error);
            SetPauseState(fdi, true);
            fdi.IsContinue = true;
            ReleaseDownloadSlot(fdi);
            return;
        }

        if (fdi.IsPause)
        {
            fdi.IsContinue = true;
            ReleaseDownloadSlot(fdi);
            return;
        }

        if (tmpkey.Status != 0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件:{fdi.Name} 获取临时下载密钥失败:{tmpkey.Msg}"),
                type: NotificationType.Error);
            ReleaseDownloadSlot(fdi);
            await MoveDownloadToFailureHistoryAsync(fdi, tmpkey.Msg);
            return;
        }

        fdi.fileTransmissionService = this;

        //开始下载
        await fdi.DownloadService.DownloadFileTaskAsync(
            _appConfigService.Config.ServerIp + $"/api/Files/DownLoadKey/{tmpkey.Data}",
            Path.Combine(fdi.Path, fdi.Name));
    }

    /// <summary>
    /// 下载结束/成功/失败执行这个
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status">0成功 1失败 2取消</param>
    public async Task DownloadComplete(FileDownloadInfo fdi, int status)
    {
        try
        {
            await _RemDownSemaphore.WaitAsync();
            var item = await _db.FindAsync<FileTransmissionModel>(fdi.Id);
            if (item == null)
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"数据库层结束失败:该文件id不存在{fdi.Id}"),
                    type: NotificationType.Error);
                return;
            }

            switch (status)
            {
                case 0:
                    item.IsEnd = true;
                    item.EndTime = DateTime.Now;
                    FileDownloadInfos.Remove(fdi);
                    MarkTransferTaskCompleted(fdi.Id);
                    break;
                case 1:
                    SetPauseState(fdi, true);
                    fdi.IsContinue = true;
                    item.StartTime = DateTime.Now;
                    break;
                case 2:
                    item.IsEnd = true;
                    item.EndTime = DateTime.Now;
                    item.IsDel = true;
                    FileDownloadInfos.Remove(fdi);
                    RemoveTransferTask(fdi.Id);
                    break;
                default:
                    return;
            }

            OnPropertyChanged(nameof(IsConditionMetDownload));
            await _db.UpdateAsync(item, typeof(FileTransmissionModel));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        finally
        {
            _RemDownSemaphore.Release();
            //防止超额释放
            if (_activeDownloads.TryRemove(fdi.Id, out _))
            {
                _downloadSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// 暂停或继续任务
    /// </summary>
    public async Task PauseOrResume(FileDownloadInfo fdi)
    {
        // 以任务自身的 IsPause 为准，不能使用 DownloadService.IsPaused。
        // 等待并发名额的任务还没有真正启动 DownloadService，内部状态不能代表用户意图。
        if (!fdi.IsPause)
        {
            fdi.BytesPerSecondSpeed = 0;
            SetPauseState(fdi, true);
            fdi.DownloadService.Pause();

            if (_activeDownloads.TryRemove(fdi.Id, out _))
            {
                _downloadSemaphore.Release();
            }

            _ = DownloadUpdataprogress(fdi.Id, fdi.CurrentSizeBytes);
            return;
        }

        // 从这里开始都是“继续”操作，先立即从暂停统计中移除。
        SetPauseState(fdi, false);

        if (fdi.IsContinue)
        {
            fdi.IsContinue = false;
            _ = DownloadInterruptResume(fdi);
            return;
        }

        // 还没有真正进入 DownloadFileTaskAsync，说明原始入队流程仍在等待名额或初始化。
        // 这里只恢复内部暂停门即可，不能再创建第二个 WaitAsync，否则同一任务会重复占用并发名额。
        if (!fdi.DownloadService.IsBusy)
        {
            fdi.DownloadService.Resume();
            _ = DownloadUpdataprogress(fdi.Id, fdi.CurrentSizeBytes);
            return;
        }

        await _downloadSemaphore.WaitAsync();
        _activeDownloads.TryAdd(fdi.Id, true);
        fdi.DownloadService.Resume();

        //暂停或继续的时候更新进度
        _ = DownloadUpdataprogress(fdi.Id, fdi.CurrentSizeBytes);
    }

    /// <summary>
    /// 取消下载任务
    /// </summary>
    public async Task DownloadCancelAsync(FileDownloadInfo fdi)
    {
        await DownloadComplete(fdi, 2);
        fdi.DownloadService.CancelAsync();
        await Task.Delay(3000);
        File.Delete(Path.Combine(_appConfigService.Config.DownloadLocation, $"{fdi.Name}.download"));
    }

    /// <summary>
    /// 更新下载进度
    /// </summary>
    public async Task DownloadUpdataprogress(Guid id, ulong progress)
    {
        string sql = $"UPDATE FileTransmissionModel SET CurrentSizeBytes = {progress} WHERE Id = '{id}'";
        // ExecuteAsync 返回受影响的行数
        int rowsAffected = await _db.ExecuteAsync(sql);
    }


    /// <summary>
    /// 暂停开始所有下载任务
    /// </summary>
    /// <param name="pause"></param>
    public async Task DownloadPauseOrResumeAll(bool pause)
    {
        if (pause)
        {
            int suo = 0;
            foreach (var item in FileDownloadInfos)
            {
                if (item.IsPause == false)
                {
                    item.BytesPerSecondSpeed = 0;
                    SetPauseState(item, true);
                    item.DownloadService.Pause();
                    if (_activeDownloads.TryRemove(item.Id, out _))
                    {
                        suo++;
                    }
                }
            }

            if (suo >= 1)
            {
                _downloadSemaphore.Release(suo);
            }
        }
        else
        {
            foreach (var item in FileDownloadInfos)
            {
                // 只拦截并处理真正处于暂停状态的任务 而不是处于等待状态 和 下载状态
                if (item.IsPause)
                {
                    SetPauseState(item, false);
                    //断点续传 需要重启发起请求 以免请求临时地址过期或流丢失
                    if (item.IsContinue)
                    {
                        _ = DownloadInterruptResume(item);
                        item.IsContinue = false;
                        continue;
                    }

                    _ = Task.Run(async () =>
                    {
                        await _downloadSemaphore.WaitAsync();

                        // 再次确认状态 排队期间如果被点了暂停，立刻丢弃名额退出
                        if (item.IsPause)
                        {
                            _downloadSemaphore.Release();
                            return;
                        }

                        _activeDownloads.TryAdd(item.Id, true);
                        item.DownloadService.Resume();
                    });
                }
            }
        }
    }


    /// <summary>
    /// 添加上传任务
    /// </summary>
    /// <param name="ufi"></param>
    public async Task AddUpload(FileUploadInfo fui)
    {
        //尝试闪存
        var re = await _webApiService.FileApi.SaveToFileAsync(fui.UploadFolderId, null, fui.Hash256);

        if (re.Status == 0)
        {
            //成功+1 并通知
            WeakReferenceMessenger.Default.Send(new UploadPanleProgressBarMsg(0));
            Home.GlobalToastManager?.Show(
                new Toast($"文件: {fui.Name} 闪传成功~"),
                type: NotificationType.Success
            );
            return;
        }

        //检测是否重复下载
        if (FileUploadInfos.Any(x => x.Hash256 == fui.Hash256))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {fui.Name} 上传任务已存在"),
                type: NotificationType.Warning);
            return;
        }

        if (!File.Exists(fui.Path))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {fui.Name} 不存在"),
                type: NotificationType.Error);
            return;
        }


        WeakReferenceMessenger.Default.Send(new UploadPanleProgressBarMsg(0));
        fui.fileTransmissionService = this;
        fui.IsPause = false;
        //加入下载列表
        FileUploadInfos.Add(fui);
        TrackTransferTask(fui.Id, fui.IsPause);
        OnPropertyChanged(nameof(IsConditionMetUpload));
        //入库
        await _db.InsertAsync(fui.ToFileTransmissionModel(), typeof(FileTransmissionModel));
        await _uploadSemaphore.WaitAsync();
        // 排到队发现被暂停  则返回 并且加入断点续传
        if (fui.IsPause)
        {
            _uploadSemaphore.Release();
            fui.IsContinue = true; // 标记为需要断点续传，方便后续恢复
            return;
        }

        // 从取得并发名额开始就登记为活动任务，暂停时才能准确归还名额。
        _activeUploads.TryAdd(fui.Id, true);

        // 先创建并持久化服务端上传会话。即使用户在请求期间暂停或关闭应用，
        // 下次启动也能使用同一个 UploadId 查询服务端已经完成的分片。
        if (!await EnsureUploadSessionAsync(fui))
        {
            SetPauseState(fui, true);
            fui.IsContinue = true;
            ReleaseUploadSlot(fui);
            return;
        }

        if (fui.IsPause)
        {
            fui.IsContinue = true;
            ReleaseUploadSlot(fui);
            return;
        }

        //开始上传
        await fui.UploadService.UploadFileTaskAsync(
            fui.UploadId, fui.Path);
    }

    /// <summary>
    /// 确保上传任务拥有已经写入本地数据库的服务端会话 ID。
    /// </summary>
    private async Task<bool> EnsureUploadSessionAsync(FileUploadInfo fui)
    {
        if (!string.IsNullOrWhiteSpace(fui.UploadId))
            return true;

        try
        {
            var result = await _webApiService.FileApi.CreateMultipartUpload(
                new MultipartUploadInitRequest(
                    fui.Name,
                    fui.TotalSizeBytes,
                    fui.Hash256));

            string? uploadId = result.Data?.ToString();
            if (result.Status != 0 ||
                string.IsNullOrWhiteSpace(uploadId) ||
                !Guid.TryParse(uploadId, out _))
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"文件 {fui.Name} 创建分片上传任务失败：{result.Msg}"),
                    type: NotificationType.Error);
                return false;
            }

            var dbItem = await _db.FindAsync<FileTransmissionModel>(fui.Id);
            if (dbItem is null)
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"文件 {fui.Name} 的本地上传记录不存在，无法保存断点信息"),
                    type: NotificationType.Error);
                return false;
            }

            fui.UploadId = uploadId;
            dbItem.UploadId = uploadId;
            dbItem.LocalFilePath = fui.Path;
            await _db.UpdateAsync(dbItem, typeof(FileTransmissionModel));
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {fui.Name} 创建分片上传任务失败：{exception.Message}"),
                type: NotificationType.Error);
            return false;
        }
    }

    private void ReleaseUploadSlot(FileUploadInfo fui)
    {
        if (_activeUploads.TryRemove(fui.Id, out _))
            _uploadSemaphore.Release();
    }

    /// <summary>
    /// 中断继续上传
    /// </summary>
    /// <param name="fdi"></param>
    private async Task UploadInterruptResume(FileUploadInfo fui)
    {
        //检测文件是否存在
        if (!File.Exists(fui.Path))
        {
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {fui.Name} 不存在"),
                type: NotificationType.Error);
            var item = await _db.FindAsync<FileTransmissionModel>(fui.Id);
            item.EndTime = DateTime.Now;
            item.IsEnd = true;
            item.ReasonFailure = "文件不存在";
            await _db.UpdateAsync(item, typeof(FileTransmissionModel));
            FileUploadInfos.Remove(fui);
            return;
        }


        await _uploadSemaphore.WaitAsync();
        //排到队发现被暂停  则返回 并且加入断点续传
        if (fui.IsPause)
        {
            _uploadSemaphore.Release();
            fui.IsContinue = true;
            return;
        }

        _activeUploads.TryAdd(fui.Id, true);

        if (!await EnsureUploadSessionAsync(fui))
        {
            SetPauseState(fui, true);
            fui.IsContinue = true;
            ReleaseUploadSlot(fui);
            return;
        }

        DefaultMsg<UploadTaskModel> data;
        try
        {
            // 冷启动恢复必须以服务端为准，不能使用本地数据库里可能滞后的字节数。
            data = await _webApiService.FileApi.GetFileChunkInfo(fui.UploadId);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Home.GlobalToastManager?.Show(
                new Toast($"文件 {fui.Name} 获取服务端上传进度失败：{exception.Message}"),
                type: NotificationType.Error);
            SetPauseState(fui, true);
            fui.IsContinue = true;
            ReleaseUploadSlot(fui);
            return;
        }

        if (fui.IsPause)
        {
            fui.IsContinue = true;
            ReleaseUploadSlot(fui);
            return;
        }

        if (data.Status != 0 || data.Data is null)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取分片数据失败，请重新上传:{data.Msg}"),
                type: NotificationType.Error);
            ReleaseUploadSlot(fui);
            var item = await _db.FindAsync<FileTransmissionModel>(fui.Id);
            if (item is not null)
            {
                item.EndTime = DateTime.Now;
                item.IsEnd = true;
                item.ReasonFailure = data.Msg;
                await _db.UpdateAsync(item, typeof(FileTransmissionModel));
            }
            FileUploadInfos.Remove(fui);
            return;
        }

        fui.fileTransmissionService = this;
        int[] completedChunkIndexes =
            data.Data.UploadedChunks
                .Select(x => x.Index)
                .Distinct()
                .ToArray();

        // 先把服务端确认的进度同步到界面和数据库，再启动剩余分片上传。
        // Size 来自服务器，能避免应用退出前最后一次本地进度尚未来得及落库的问题。
        ulong serverUploadedBytes = data.Data.UploadedChunks
            .GroupBy(x => x.Index)
            .Aggregate(
                0UL,
                (total, group) => checked(total + (ulong)Math.Max(0, group.Last().Size)));
        serverUploadedBytes = Math.Min(serverUploadedBytes, fui.TotalSizeBytes);
        fui.CurrentSizeBytes = serverUploadedBytes;
        fui.ProgressPercentage = fui.TotalSizeBytes == 0
            ? 0
            : Math.Round((double)serverUploadedBytes / fui.TotalSizeBytes, 4) * 100;
        await UploadUpdataprogress(fui.Id, serverUploadedBytes);

        await fui.UploadService.UploadFileTaskAsync(
            fui.UploadId,
            fui.Path,
            completedChunkIndexes);
    }


    /// <summary>
    ///  上传结束/成功/失败执行这个
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status">0成功 1失败 2取消</param>
    public async Task UploadComplete(FileUploadInfo fud, int status)
    {
        try
        {
            await _RemUpSemaphore.WaitAsync();
            var item = await _db.FindAsync<FileTransmissionModel>(fud.Id);
            if (item == null)
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"数据库层结束失败:该文件id不存在{fud.Id}"),
                    type: NotificationType.Error);
                return;
            }

            switch (status)
            {
                case 0:

                    var r = await _webApiService.FileApi.MergeFiles(fud.UploadId.ToString(), fud.UploadFolderId);
                    if (r.Status != 0)
                    {
                        item.ReasonFailure = r.Msg;
                        break;
                    }

                    item.IsEnd = true;
                    item.EndTime = DateTime.Now;
                    FileUploadInfos.Remove(fud);
                    MarkTransferTaskCompleted(fud.Id);
                    break;
                case 1:
                    item.EndTime = DateTime.Now;
                    item.ReasonFailure = fud.ErrorMsg;
                    SetPauseState(fud, true);
                    fud.IsContinue = true;
                    break;
                case 2:
                    item.IsEnd = true;
                    item.EndTime = DateTime.Now;
                    item.IsDel = true;
                    FileUploadInfos.Remove(fud);
                    RemoveTransferTask(fud.Id);
                    break;
                default:
                    return;
            }

            OnPropertyChanged(nameof(IsConditionMetUpload));
            await _db.UpdateAsync(item, typeof(FileTransmissionModel));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        finally
        {
            _RemUpSemaphore.Release();
            //防止超额释放
            if (_activeUploads.TryRemove(fud.Id, out _))
            {
                _uploadSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// 暂停或继续上传任务
    /// </summary>
    public async Task PauseOrResumeUpload(FileUploadInfo fui)
    {
        // 与下载一致，以任务自身的 IsPause 作为用户暂停状态。
        // 等待上传并发名额时 UploadService 还没有真正开始，不能用它的 IsPaused 判断按钮意图。
        if (!fui.IsPause)
        {
            fui.BytesPerSecondSpeed = 0;
            SetPauseState(fui, true);
            fui.UploadService.Pause();

            if (_activeUploads.TryRemove(fui.Id, out _))
            {
                _uploadSemaphore.Release();
            }

            _ = UploadUpdataprogress(fui.Id, fui.CurrentSizeBytes);
            return;
        }

        // 从这里开始都是“继续”操作，先立即从暂停统计中移除。
        SetPauseState(fui, false);

        if (fui.IsContinue)
        {
            fui.IsContinue = false;
            _ = UploadInterruptResume(fui);
            return;
        }

        // 原始上传流程仍在等待并发名额或创建分片任务时，不重复进入 WaitAsync。
        if (!fui.UploadService.IsRunning)
        {
            _ = UploadUpdataprogress(fui.Id, fui.CurrentSizeBytes);
            return;
        }

        await _uploadSemaphore.WaitAsync();
        _activeUploads.TryAdd(fui.Id, true);
        fui.UploadService.Resume();

        //暂停或继续的时候更新进度
        _ = UploadUpdataprogress(fui.Id, fui.CurrentSizeBytes);
    }

    /// <summary>
    /// 取消上传任务
    /// </summary>
    public async Task UploadCancelAsync(FileUploadInfo fui)
    {
        await UploadComplete(fui, 2);
        fui.UploadService.CancelAsync();
    }

    /// <summary>
    /// 更新上传进度
    /// </summary>
    public async Task UploadUpdataprogress(Guid id, ulong progress)
    {
        string sql = $"UPDATE FileTransmissionModel SET CurrentSizeBytes = {progress} WHERE Id = '{id}'";
        // ExecuteAsync 返回受影响的行数
        int rowsAffected = await _db.ExecuteAsync(sql);
    }


    /// <summary>
    /// 暂停开始所有上传任务
    /// </summary>
    /// <param name="pause"></param>
    public async Task UploadPauseOrResumeAll(bool pause)
    {
        if (pause)
        {
            int suo = 0;
            foreach (var item in FileUploadInfos)
            {
                if (item.IsPause == false)
                {
                    item.BytesPerSecondSpeed = 0;
                    SetPauseState(item, true);
                    item.UploadService.Pause();
                    if (_activeUploads.TryRemove(item.Id, out _))
                    {
                        suo++;
                    }
                }
            }

            if (suo >= 1)
            {
                _uploadSemaphore.Release(suo);
            }
        }
        else
        {
            foreach (var item in FileUploadInfos)
            {
                // 只拦截并处理真正处于暂停状态的任务 而不是处于等待状态 和 下载状态
                if (item.IsPause)
                {
                    SetPauseState(item, false);
                    //断点续传 需要重启发起请求 以免请求临时地址过期或流丢失
                    if (item.IsContinue)
                    {
                        _ = UploadInterruptResume(item);
                        item.IsContinue = false;
                        continue;
                    }

                    _ = Task.Run(async () =>
                    {
                        await _uploadSemaphore.WaitAsync();

                        // 再次确认状态 排队期间如果被点了暂停，立刻丢弃名额退出
                        if (item.IsPause)
                        {
                            _uploadSemaphore.Release();
                            return;
                        }

                        _activeUploads.TryAdd(item.Id, true);
                        item.UploadService.Resume();
                    });
                }
            }
        }
    }

    /// <summary>
    /// 添加一个传输任务。
    /// 同一个任务 ID 重复调用不会重复计数。
    /// </summary>
    private void TrackTransferTask(
        Guid taskId,
        bool isPaused)
    {
        bool changed;

        lock (_taskStatisticsLock)
        {
            bool trackedChanged =
                _trackedTaskIds.Add(taskId);

            bool pausedChanged = isPaused
                ? _pausedTaskIds.Add(taskId)
                : _pausedTaskIds.Remove(taskId);

            changed = trackedChanged || pausedChanged;
        }

        if (changed)
            RefreshTransferTaskStatistics();
    }

    /// <summary>
    /// 标记任务成功完成。
    /// 重复完成回调不会重复增加完成数。
    /// </summary>
    private void MarkTransferTaskCompleted(Guid taskId)
    {
        bool changed;
        Interlocked.Increment(ref _overTsTaskDayNum);
        OnPropertyChanged(nameof(OverTsTaskDayNum));
        lock (_taskStatisticsLock)
        {
            if (!_trackedTaskIds.Contains(taskId))
                return;

            bool completedChanged =
                _completedTaskIds.Add(taskId);

            bool pausedChanged =
                _pausedTaskIds.Remove(taskId);

            changed =
                completedChanged || pausedChanged;
        }

        if (changed)
            RefreshTransferTaskStatistics();
    }

    /// <summary>
    /// 用户取消任务或永久删除任务。
    /// 总数和剩余数都会减少。
    /// </summary>
    private void RemoveTransferTask(Guid taskId)
    {
        bool changed;

        lock (_taskStatisticsLock)
        {
            bool removedTracked =
                _trackedTaskIds.Remove(taskId);

            bool removedCompleted =
                _completedTaskIds.Remove(taskId);

            bool removedPaused =
                _pausedTaskIds.Remove(taskId);

            changed =
                removedTracked ||
                removedCompleted ||
                removedPaused;
        }

        if (changed)
            RefreshTransferTaskStatistics();
    }

    /// <summary>
    /// 刷新公开统计属性。
    /// 保证属性通知在 Avalonia UI 线程执行。
    /// </summary>
    private void RefreshTransferTaskStatistics()
    {
        void Refresh()
        {
            int total;
            int completed;
            int paused;

            lock (_taskStatisticsLock)
            {
                total = _trackedTaskIds.Count;
                completed = _completedTaskIds.Count;
                paused = _pausedTaskIds.Count;
            }

            TotalTsTaskNum = total;
            OverTsTaskNum = completed;
            TotalPauseNum = paused;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    /// <summary>
    /// 开始新一轮统计时手动清空。
    /// </summary>
    public void ResetTransferTaskStatistics()
    {
        lock (_taskStatisticsLock)
        {
            _trackedTaskIds.Clear();
            _completedTaskIds.Clear();
            _pausedTaskIds.Clear();
        }

        RefreshTransferTaskStatistics();
    }

    /// <summary>
    /// 历史记录翻页
    /// </summary>
    public async Task LoadNextTsPage(bool isInit = false)
    {
        if (isInit)
        {
            pageSkip = 0;
            FileHistoryInfos.Clear();
            
        }
       var over =await _db.Table<FileTransmissionModel>().Where(x => x.Uid == _userInfoService.ShowUserInfo.UserId && x.IsEnd==true && x.IsDel == false)
            .OrderByDescending(x => x.EndTime).Skip(pageSkip).Take(20).ToArrayAsync();
       pageSkip += 20;
        foreach (var item in over)
        {
            if (item.Type == 0)
            {
                FileHistoryInfos.Add(new FileHisDownInfo()
                {
                    Id = item.Id,
                    EndTime = item.EndTime,
                    IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(item.FileName),
                    Name = item.FileName,
                    Path = item.LocalFilePath,
                    TotalSizeBytes = item.TotalSizeBytes,
                    ReasonFailure = item.ReasonFailure
                });
            }
            else
            {
                FileHistoryInfos.Add(new FileHisUpInfo()
                {
                    Id = item.Id,
                    EndTime = item.EndTime,
                    IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(item.FileName),
                    Name = item.FileName,
                    TotalSizeBytes = item.TotalSizeBytes,
                    UploadFolderId = item.UploadFolderId,
                    UploadFolderPath = item.UploadFolderPath
                });
            }
        }
        OnPropertyChanged(nameof(IsConditionMetHis));
    }

    /// <summary>
    /// 删除历史记录
    /// </summary>
    /// <param name="item"></param>
    public async Task DelTsInfo(Object item,bool isDelAll = false)
    { 
        string sql=null;
        if (isDelAll)
        {
            FileHistoryInfos.Clear();
            sql = $"UPDATE FileTransmissionModel SET IsDel = true WHERE Uid = '{_userInfoService.ShowUserInfo.UserId}' and  IsDel=false";
        }
        else
        {
            FileHistoryInfos.Remove(item);
           
            if (item is FileHisUpInfo )
            {
                string id = (item as FileHisUpInfo).Id.ToString();
                sql = $"UPDATE FileTransmissionModel SET IsDel = true WHERE Id = '{id}'";
            }

            if (item is FileHisDownInfo)
            {
                string id = (item as FileHisDownInfo).Id.ToString();
                sql = $"UPDATE FileTransmissionModel SET IsDel = true WHERE Id = '{id}'";
            }
        }
        
       
        OnPropertyChanged(nameof(IsConditionMetHis));
        // ExecuteAsync 返回受影响的行数
        int rowsAffected = await _db.ExecuteAsync(sql);
    }
    
    /// <summary>
    /// 统一暂停状态 方便统计
    /// </summary>
    /// <param name="taskId"></param>
    /// <param name="isPaused"></param>
    private void SetTransferTaskPaused(Guid taskId, bool isPaused)
    {
        bool changed;

        lock (_taskStatisticsLock)
        {
            // 已经被取消、删除或者完成的任务不统计暂停。
            if (!_trackedTaskIds.Contains(taskId) ||
                _completedTaskIds.Contains(taskId))
            {
                return;
            }

            changed = isPaused
                ? _pausedTaskIds.Add(taskId)
                : _pausedTaskIds.Remove(taskId);
        }

        if (changed)
            RefreshTransferTaskStatistics();
    }

    private void SetPauseState(
        FileDownloadInfo item,
        bool isPaused)
    {
        item.IsPause = isPaused;
        SetTransferTaskPaused(item.Id, isPaused);
    }

    private void SetPauseState(
        FileUploadInfo item,
        bool isPaused)
    {
        item.IsPause = isPaused;
        SetTransferTaskPaused(item.Id, isPaused);
    }

    /// <summary>
    /// 获取今日成功数
    /// </summary>
    private async Task GetDayOverTaskNum()
    {
      OverTsTaskDayNum = await _db.Table<FileTransmissionModel>().Where(x =>
            x.Uid == _userInfoService.ShowUserInfo.UserId && x.IsEnd == true && x.IsDel == false && x.EndTime>=DateTime.Today).CountAsync();
    }
}
/*
 * 1. 信号量（SemaphoreSlim）的精准生命周期控制
 * 系统使用 SemaphoreSlim 来严格限制同时下载（_downloadSemaphore）和上传（_uploadSemaphore）的并发数量。
 * 难点：信号量的“借”与“还”极易失衡。初始化参数错误（如 initialCount 为 0）会导致永久死锁；而在多线程环境下重复调用 Release() 会引发 SemaphoreFullException 导致程序崩溃。
 * 破局点：将信号量的释放逻辑严格收拢，并结合任务的终态（完成、失败、取消）在 DownloadComplete 的 finally 块中进行统一的安全释放。
 *
 * 2. 真实活跃状态的追踪与“信号量泄露”防御
 * 在批量控制时，任务存在“正在下载”和“排队等待”两种隐形状态，盲目释放信号量会导致排队任务被意外唤醒（幽灵任务）。
 * 破局点：引入了线程安全的字典 ConcurrentDictionary<Guid, bool> _activeDownloads 来充当实际已经开始下载的名单——“下载活动名单”。
 * 只有真正在字典中注册成功的任务，在暂停或结束时才有资格执行 _downloadSemaphore.Release()。这种设计彻底杜绝了狂点“全部开始/暂停”导致的名额吞噬和死锁问题。
 *
 * 3. 异步编程中的“时间差”与二次校验
 * 在向服务器请求下载密钥 GetFileDownLoadTempKeyAsync 时存在网络延迟（数百毫秒）。
 * 难点：在这段延迟期间，用户可能已经点击了“取消”或“暂停”。如果网络请求结束后直接无脑执行下载，就会产生脱离管控的“幽灵下载”。
 * 破局点：在所有存在 await 耗时操作的下方，强制引入了二次状态确认机制。例如请求结束后检查 if (item.IsPause)，若状态已变则立刻主动归还名额并退出。
 *
 * 4. 循环队列的非阻塞调度
 * 在执行“全部开始”或批量添加任务时，需要同时处理多个任务。
 * 难点：如果在 foreach 循环中直接使用 await _downloadSemaphore.WaitAsync()，第一个排队的任务会直接把整个主线程或循环堵死，导致后续任务连 UI 状态都无法更新。
 * 破局点：采用“状态先行，后台排队”的策略。在 DownloadPauseOrResumeAll 中，将获取信号量和恢复下载的逻辑包裹在 Task.Run(...) 中，实现非阻塞式的并发排队调度。
 */
