using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using LiveChartsCore.Defaults;

namespace drive_desktop.ViewModels;

public partial class FileTransmissionViewModel : ViewModelBase
{
    [ObservableProperty] private int _selectPage = 0;

    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    public ObservableCollection<ObservablePoint> UpValues { get; } = new();
    public ObservableCollection<ObservablePoint> DownValues { get; } = new();

    private const int WindowSize = 5;

    private double _nextUploadX;
    private double _nextDownloadX;

    [ObservableProperty] private double _xMin;
    [ObservableProperty] private double _xMax = WindowSize - 1;
    [ObservableProperty] private double _yMax = 1;
    /// <summary>
    /// 总上传下载流量
    /// </summary>
    [ObservableProperty] private long _totalDownloadTraffic =0;
    [ObservableProperty] private long _totalUploadTraffic = 0;
    /// <summary>
    /// Pause数量
    /// </summary>
    [ObservableProperty] private long _pauseNum = 0;
    [ObservableProperty]
    private FileTransmissionService _fileTransmissionService;
    private readonly ITopLevelProvider _topLevelProvider;
    private readonly AppConfigService _appConfigService;
    /// <summary>
    /// 已使用容量
    /// </summary>
    [ObservableProperty]
    private long _usedSpaceInBytes = 0;
    /// <summary>
    /// 总容量
    /// </summary>
    [ObservableProperty]
    private long _totalSpaceInBytes = 0;
    /// <summary>
    /// 剩余容量
    /// </summary>
    [ObservableProperty]
    private long _freeSpaceInBytes = 0;
    /// <summary>
    /// 剩余百分比
    /// </summary>
    [ObservableProperty]
    private int _spacePercentage = 0;
    
    /// <summary>
    /// 本地下载目录已使用容量
    /// </summary>
    [ObservableProperty]
    private long _localUsedSpaceInBytes = 0;
    /// <summary>
    /// 本地下载目录总容量
    /// </summary>
    [ObservableProperty]
    private long _localTotalSpaceInBytes = 0;
    /// <summary>
    /// 本地下载目录剩余容量
    /// </summary>
    [ObservableProperty]
    private long _localFreeSpaceInBytes = 0;
    /// <summary>
    /// 本地下载目录剩余百分比
    /// </summary>
    [ObservableProperty]
    private int _localSpacePercentage = 0;
      public FileTransmissionViewModel()
      {
         /*  _fileTransmissionService = new FileTransmissionService();
        _fileTransmissionService.FileDownloadInfos = new ObservableCollection<FileDownloadInfo>
        {
            // 1. 已完成的 Word 文档
            new FileDownloadInfo
            {
                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo("2026年度财务报表.docx"),
                FileId = Guid.NewGuid().ToString("N"),
                Name = "2026年度财务报表.docx",
                Path = @"D:\Downloads\2026年度财务报表.docx",
                TotalSizeBytes = 2548000, // 约 2.4 MB
                CurrentSizeBytes = 2548000, // 下载完成，当前大小 = 总大小
                Hash256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                IsEnd = true,
                EndTime = DateTime.Now.AddMinutes(-30) // 30分钟前下载完成
            },

            // 2. 正在下载的设计图 (图片)
            new FileDownloadInfo
            {
                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo("UI界面高保真设计稿.png"),
                FileId = Guid.NewGuid().ToString("N"),
                Name = "UI界面高保真设计稿.png",
                Path = @"D:\Downloads\UI界面高保真设计稿.png",
                TotalSizeBytes = 15728640, // 约 15 MB
                CurrentSizeBytes = 8388608, // 已下载约 8 MB (进度 50% 左右)
                Hash256 = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92",
                IsEnd = false,
                EndTime = DateTime.MinValue // 还没下载完，没有结束时间
            },

            // 3. 正在下载的超大文件 (视频)
            new FileDownloadInfo
            {
                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo("项目第一阶段汇报演示.mp4"),
                FileId = Guid.NewGuid().ToString("N"),
                Name = "项目第一阶段汇报演示.mp4",
                Path = @"D:\Downloads\项目第一阶段汇报演示.mp4",
                TotalSizeBytes = 1073741824, // 约 1 GB
                CurrentSizeBytes = 104857600, // 已下载约 100 MB
                Hash256 = "4e1243bd22c66e76c2ba9eddc1f91394e57f9f83",
                IsEnd = false,
                EndTime = DateTime.MinValue
            },

            // 4. 已完成的压缩包
            new FileDownloadInfo
            {
                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo("前台源码备份_202607.zip"),
                FileId = Guid.NewGuid().ToString("N"),
                Name = "前台源码备份_202607.zip",
                Path = @"D:\Downloads\前台源码备份_202607.zip",
                TotalSizeBytes = 52428800, // 约 50 MB
                CurrentSizeBytes = 52428800,
                Hash256 = "72f10b740523f3e1b7b7194689255abec9833f4a9b5abf8691f3a532788e0018",
                IsEnd = true,
                EndTime = DateTime.Now.AddDays(-1) // 昨天下载完成的
            },

            // 5. 已完成的 PDF 文档
            new FileDownloadInfo
            {
                IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo("Avalonia_UI_开发指南.pdf"),
                FileId = Guid.NewGuid().ToString("N"),
                Name = "Avalonia_UI_开发指南.pdf",
                Path = @"D:\Downloads\Avalonia_UI_开发指南.pdf",
                TotalSizeBytes = 12, // 约 8.2 MB
                CurrentSizeBytes = 12,
                Hash256 = "b7a875fc29352125bb7ea740fa3d0bc4f3d2f97c",
                IsEnd = true,
                EndTime = DateTime.Now.AddHours(-2)
            }
        };*/
      }

      /// <summary>
      /// 定时任务循环
      /// </summary>
      private bool _scheduledTask = false;
      
    public FileTransmissionViewModel( AppConfigService appConfigService,ITopLevelProvider topBarViewModel,FileTransmissionService fileTransmissionService, UserInfoService userInfoService,
        WebApiService webApiService)
    {
        _appConfigService = appConfigService;
        _topLevelProvider = topBarViewModel;
        _userInfoService = userInfoService;
        _webApiService = webApiService;
        _fileTransmissionService = fileTransmissionService;
        //用来检测页面是否在传输界面 不然就无需启用定时任务统计
        WeakReferenceMessenger.Default.Register<SidebarItemMessage>(this,
            (recipient, message) =>
            {
                if (message.index==2)
                {
                    if (_scheduledTask)
                    {
                        return;
                    }
                    _scheduledTask = true;
                    TrafficStatistics();
                }
                else
                {
                    _scheduledTask = false;
                }
            });
    
    }


    /// <summary>
    /// 切换下载上传历史页
    /// </summary>
    /// <param name="page"></param>
    [RelayCommand]
    private void SwitchPage(string page)
    {
        SelectPage = int.Parse(page);
    }
    /// <summary>
    /// 暂停继续下载或上传任务
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void PauseOrResume(Object item)
    {
        if (item is FileDownloadInfo )
        {
            _ = FileTransmissionService.PauseOrResume(item as FileDownloadInfo);
        }
        else if (item is FileUploadInfo)
        {
            _ = FileTransmissionService.PauseOrResumeUpload(item as FileUploadInfo);
        }
       
    }
    
    /// <summary>
    /// 取消下载上传任务
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void Cancel(Object item)
    {
        if (item is FileDownloadInfo )
        {
            _ = FileTransmissionService.DownloadCancelAsync(item as FileDownloadInfo);
        }
        else if (item is FileUploadInfo)
        {
            _ = FileTransmissionService.UploadCancelAsync(item as FileUploadInfo);
        }
        
    }

    /// <summary>
    /// 暂停继续所有任务
    /// </summary>
    /// <param name="pause">0是暂停 1是继续</param>
    [RelayCommand]
    private void DownloadPauseOrResumeAll(string pause)
    {
        //检测是否是下载页
        if (SelectPage == 0)
        {
         
            FileTransmissionService.DownloadPauseOrResumeAll(pause == "0");
        }
        else
        {
            FileTransmissionService.UploadPauseOrResumeAll(pause == "0");
        }
      
    }
    /// <summary>
    /// 定时刷新总流量显示
    /// </summary>
    private async Task TrafficStatistics()
    {
        DownloadTrafficStatistics();
        UploadTrafficStatistics();
        CapacityDetectionTask();
    }

    private async Task DownloadTrafficStatistics()
    {
        while (true)
        {
            if (_scheduledTask == false)
            {
                return;
            }
            var s = FileTransmissionService.FileDownloadInfos.Where(x => x.IsPause == false).ToArray();
            if (s.Length > 0)
            {
                TotalDownloadTraffic = (long)s.Sum(x => x.BytesPerSecondSpeed);
            }
            else
            {
                TotalDownloadTraffic = 0;
            }

            AddDownloadValue(TotalDownloadTraffic);
            await Task.Delay(1000);
        }
    }
    private async Task UploadTrafficStatistics()
    {
        while (true)
        {
            if (_scheduledTask == false)
            {
                return;
            }
            var s = FileTransmissionService.FileUploadInfos.Where(x => x.IsPause == false).ToArray();
            if (s.Length > 0)
            {
                TotalUploadTraffic = (long)s.Sum(x => x.BytesPerSecondSpeed);
            }
            else
            {
                TotalUploadTraffic = 0;
            }   
            AddUploadValue(TotalUploadTraffic);
            await Task.Delay(1000);
        }
    }
    
    /// <summary>
    /// 加载下一页历史
    /// </summary>
    [RelayCommand]
    private async Task LoadNextTsPage()
    {
       await _fileTransmissionService.LoadNextTsPage();
    }

    
    /// <summary>
    /// 历史记录 打开文件夹
    /// </summary>
    [RelayCommand]
    private async Task OpenFileLocal(object item)
    {
        if (item is FileHisUpInfo)
        {
            WeakReferenceMessenger.Default.Send(new FilePageMessage((item as FileHisUpInfo).UploadFolderPath));
            WeakReferenceMessenger.Default.Send(new SidebarItemMessage(0, "我的文件"));
        }
        if (item is FileHisDownInfo)
        {
            var path = (item as FileHisDownInfo).Path;
            if (!Directory.Exists(path))
                return;

            var topLevel =_topLevelProvider.GetTopLevel();
            if (topLevel is null)
                return;

            var success = await topLevel.Launcher.LaunchDirectoryInfoAsync(
                new DirectoryInfo(path));
         
        }
    }
    /// <summary>
    /// 删除历史信息
    /// </summary>
    [RelayCommand]
    private async Task DelTsInfo(object item)
    {
      await  _fileTransmissionService.DelTsInfo(item);
      
    }

    /// <summary>
    /// 翻页
    /// </summary>
    [RelayCommand]
    private async Task RefreshHis()
    {
        await _fileTransmissionService.LoadNextTsPage(true);
    }
    /// <summary>
    /// 清除所有历史记录
    /// </summary>
    [RelayCommand]
    private async Task DelHisAll()
    {
        await _fileTransmissionService.DelTsInfo(null, true);
    }
    
    private void AddUploadValue(double value)
    {
        AddValue(UpValues, value, ref _nextUploadX);
    }

    private void AddDownloadValue(double value)
    {
        AddValue(DownValues, value, ref _nextDownloadX);
    }

    private void AddValue(
        ObservableCollection<ObservablePoint> values,
        double value,
        ref double nextX)
    {
        var x = nextX++;

        values.Add(new ObservablePoint(x, value));

        while (values.Count > WindowSize)
        {
            values.RemoveAt(0);
        }

        // 两条线共用同一个图表坐标轴，取进度更快的序号。
        var newestX = Math.Max(_nextUploadX, _nextDownloadX) - 1;

        XMax = Math.Max(WindowSize - 1, newestX);
        XMin = Math.Max(0, XMax - WindowSize + 1);

        var visibleMax = UpValues
            .Concat(DownValues)
            .Select(point => point.Y ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        // 防止所有数据为 0 时产生 0~0 的无效坐标范围。
        YMax = Math.Max(1, visibleMax * 1.15);
    }

    /// <summary>
    /// 容量检测任务
    /// </summary>
    private async Task CapacityDetectionTask()
    {
        while (true)
        {
            if (_scheduledTask == false)
            {
                return;
            }
            var info =  await _webApiService.FileApi.GetUserStorageCapacityInfoAsync();
            if (info.Status==0)
            {
                UsedSpaceInBytes = (long)info.Data.UsedSpaceInBytes;
                TotalSpaceInBytes = (long)info.Data.TotalSpaceInBytes;
                FreeSpaceInBytes =  (long)info.Data.FreeSpaceInBytes;
                SpacePercentage =(int)(Math.Round((double)UsedSpaceInBytes / (double)TotalSpaceInBytes, 2) * 100);
            }

            var info2 = GetStorageInfo(_appConfigService.Config.DownloadLocation);
            LocalUsedSpaceInBytes =info2.Item2;
            LocalTotalSpaceInBytes = info2.Item1;
            LocalFreeSpaceInBytes =  info2.Item3;
            LocalSpacePercentage = (int)(Math.Round((double)LocalUsedSpaceInBytes / (double)LocalTotalSpaceInBytes, 2) * 100);
            await Task.Delay(5000);
        }
    }
    
    


    private (long,long,long) GetStorageInfo(string directoryPath)
    {
        var fullPath = Path.GetFullPath(directoryPath);
        var root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root))
            return (0, 0, 0);

        var drive = new DriveInfo(root);

        if (!drive.IsReady)
            return (0, 0, 0);

        var total = drive.TotalSize;
        var free = drive.AvailableFreeSpace;
        var used = total - free;

        return (total, used, free);
    }
}