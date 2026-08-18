using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using LiveMarkdown.Avalonia;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    /// <summary>
    /// 界面主题
    /// </summary>
    [ObservableProperty] private ThemeVariant _currentTheme = ThemeVariant.Light;

    private readonly IThemeService _themeService;

    /// <summary>
    /// 侧边栏vm
    /// </summary>
    public SidebarViewModel SidebarVM { get; }

    /// <summary>
    /// 视图添加编辑vm
    /// </summary>
    public CustomViewEditViewModel CustomViewEditViewModel { get; }

    /// <summary>
    /// 新建文件夹弹窗vm
    /// </summary>
    public NewFolderDialogViewModel NewFolderDialogViewModel { get; }

    /// <summary>
    /// 顶栏vm
    /// </summary>
    public TopBarViewModel TopBarViewModel { get; }

    /// <summary>
    /// 文件预览vm
    /// </summary>
    public FilePageViewModel FilePageViewModel { get; }

    /// <summary>
    /// 文件属性弹窗vm
    /// </summary>
    public FilePropertiesDialogViewModel FilePropertiesDialogViewModel { get; }

    /// <summary>
    /// 左侧导航页码
    /// </summary>
    [ObservableProperty] private int _pageIndex = 0;

    /// <summary>
    /// 进度条读取目录文件显示
    /// </summary>
    [ObservableProperty] private double _progressHeight = 0;

    /// <summary>
    /// 进度条
    /// </summary>
    [ObservableProperty] private int _uploadProgressBarValue = 0;

    /// <summary>
    /// 进度条
    /// </summary>
    [ObservableProperty] private int _uploadProgressBarMax = 100;

    /// <summary>
    /// 上传弹窗
    /// </summary>
    [ObservableProperty] private bool _upLoadShow = false;
    /// <summary>
    /// 快捷搜索区
    /// </summary>
    [ObservableProperty] private bool _fastSearchShow = false;

    /// <summary>
    /// loading
    /// </summary>
    [ObservableProperty] private bool _isLoaded = true;
    
    /// <summary>
    /// 简单搜索item集合
    /// </summary>
    [ObservableProperty] private ObservableCollection<FileSearchSimpleItem> _fileSearchSimpleItems;

    /// <summary>
    /// 是否显示分享文件按钮
    /// </summary>
    [ObservableProperty] private bool _shareFileButtonVisible;

    /// <summary>
    /// 分享文件名称
    /// </summary>
    [ObservableProperty] private string _shareFileName = string.Empty;

    private FileShareInfoDto? _shareFileInfo;
    private string? _shareKey;
    
    
    private readonly UserInfoService _userInfoService;
    private readonly WebApiService _webApiService;
    private readonly AppConfigService _appConfigService;
    public ImageViewModel ImageViewModel { get; }
    public TextViewDialogViewModel TextViewDialogViewModel { get; }
    public ShareFileDialogViewModel ShareFileDialogViewModel { get; }
    public SearchPageViewModel SearchPageViewModel { get; }
    public MoveFilesViewModel MoveFilesViewModel { get; }
    public FileTransmissionViewModel FileTransmissionViewModel { get; }
    public FileSharePageViewModel FileSharePageViewModel { get; }
    public DocumentViewDialogViewModel DocumentViewDialogViewModel { get; }
    public StatisticsDashboardPageViewModel StatisticsDashboardPageViewModel { get; }
    public SettingPageViewModel SettingPageViewModel { get; }
    public AiChatViewModel AiChatViewModel { get; }
    public HomeViewModel(AiChatViewModel aiChatViewModel,SettingPageViewModel settingPageViewModel,StatisticsDashboardPageViewModel statisticsDashboardPageViewModel,DocumentViewDialogViewModel documentViewDialogViewModel,FileSharePageViewModel fileSharePageViewModel,FileTransmissionViewModel fileTransmissionViewModel, SearchPageViewModel searchPageViewModel,
        ShareFileDialogViewModel shareFileDialogViewModel, TextViewDialogViewModel textViewDialogViewModel,
        ImageViewModel imageViewModel, FilePropertiesDialogViewModel filePropertiesDialogViewModel,
        MoveFilesViewModel moveFilesViewModel, FilePageViewModel filePageViewModel, WebApiService webApiService,
        AppConfigService appConfigService,
        NewFolderDialogViewModel newFolderDialogViewModel, IThemeService themeService, SidebarViewModel sidebarVM,
        CustomViewEditViewModel customViewEditViewModel, TopBarViewModel topBarViewModel,
        UserInfoService userInfoService)
    {
        AiChatViewModel = aiChatViewModel;
        TopBarViewModel = topBarViewModel;
        SettingPageViewModel =  settingPageViewModel;
        StatisticsDashboardPageViewModel = statisticsDashboardPageViewModel;
        DocumentViewDialogViewModel =  documentViewDialogViewModel;
        FileSharePageViewModel = fileSharePageViewModel;
        FileTransmissionViewModel = fileTransmissionViewModel;
        SearchPageViewModel = searchPageViewModel;
        ShareFileDialogViewModel = shareFileDialogViewModel;
        TextViewDialogViewModel = textViewDialogViewModel;
        ImageViewModel = imageViewModel;
        FilePropertiesDialogViewModel = filePropertiesDialogViewModel;
        MoveFilesViewModel = moveFilesViewModel;
        _webApiService = webApiService;
        _appConfigService = appConfigService;
        _themeService = themeService;
        _currentTheme = themeService.CurrentTheme;
        SidebarVM = sidebarVM;
        FilePageViewModel = filePageViewModel;
        CustomViewEditViewModel = customViewEditViewModel;

        _userInfoService = userInfoService;

        NewFolderDialogViewModel = newFolderDialogViewModel;


        //注册监听到主题色发生变化后的消费者
        WeakReferenceMessenger.Default.Register<ThemeService.ThemeChangedMessage>(this,
            (recipient, message) => { CurrentTheme = message.NewTheme; });
        CurrentTheme = _themeService.CurrentTheme;
        WeakReferenceMessenger.Default.Register<SidebarItemMessage>(this,
            (recipient, message) => { PageIndex = message.index; });


        WeakReferenceMessenger.Default.Register<UploadFlyoutMessage>(this,
            (recipient, message) => { UpLoadShow = message.show; });
        
        WeakReferenceMessenger.Default.Register<FastSearchFlyoutMessage>(this,
            (recipient, message) =>
            {
                IsLoaded = false;
                FastSearchShow = message.show;
                SearchSimple(message.msg);

            });

        WeakReferenceMessenger.Default.Register<UploadPanleProgressBarMsg>(this,
            (recipient, msg) =>
            {
                if (msg.type == 0)
                {
                    Interlocked.Increment(ref _uploadProgressBarValue);
                    OnPropertyChanged(nameof(UploadProgressBarValue));

                    if (UploadProgressBarValue == UploadProgressBarMax)
                    {
                        ProgressHeight = 0;
                        Home.GlobalToastManager?.Show(
                            new Toast($"上传任务已添加完成,详情请到传输面板查看~"),
                            type: NotificationType.Success
                        );
                        UpLoadShow = false;
                    }
                }
            });

    }

    public HomeViewModel()
    {
    }

    /// <summary>
    /// 进行简单搜索以及判断是否是分享链接
    /// </summary>
    private async Task SearchSimple(string  searchText)
    {
        ShareFileButtonVisible = false;
        ShareFileName = string.Empty;
        _shareFileInfo = null;
        _shareKey = null;

        //是否是分享key
        if (searchText.StartsWith("share_", StringComparison.OrdinalIgnoreCase))
        {
            var shareKey = searchText["share_".Length..].Trim();
            if (!string.IsNullOrEmpty(shareKey))
            {
                var info = await _webApiService.FileApi.GetShareInfoAsync(shareKey);
                if (info.Status == 0 )
                {
                    _shareFileInfo = info.Data;
                    _shareKey = shareKey;
                    ShareFileName = info.Data.Name;
                    ShareFileButtonVisible = true;
                }
            }
        }
        var info2 = await _webApiService.FileApi.SearchFilesAsync(new SearchInfoDTO()
        {
            Keyword = searchText
        },false);

        if (info2.Status ==0)
        {
            //搜索文件获取前10个
            var tasks =   info2.Data.FileInfos.Take(10).Select(async x =>
            {
                var p = await _webApiService.FileApi.GetFullFolderPathAsync(x.FolderId);
                string path = "路径获取失败";
                if (p.Status == 0)
                {
                    path = p.Data.ToString();
                }
                return new FileSearchSimpleItem()
                {
                    Id = x.Id,
                    Name = x.FileName,
                    IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(x.FileName),
                    Path = path
                };
            }).ToArray();
                
            var result = await Task.WhenAll(tasks);
            FileSearchSimpleItems = new ObservableCollection<FileSearchSimpleItem>(result) ;
        }
        IsLoaded = true;
    }

    /// <summary>
    /// 打开分享文件窗口
    /// </summary>
    [RelayCommand]
    private void OpenShareFile()
    {
        if (!ShareFileButtonVisible || _shareFileInfo == null || string.IsNullOrEmpty(_shareKey))
        {
            return;
        }

        var shareView = new ShareView
        {
            DataContext = new ShareViewModel(_shareFileInfo, _shareKey, _userInfoService, _appConfigService)
        };
        shareView.Show();
    }

    [RelayCommand]
    private void Close()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown(0);
        }
    }

    /// <summary>
    /// 添加上传列表
    /// </summary>
    public async Task UploadFiles( List<string> Files, List<string> Folder)
    {
        try
        {
            ProgressHeight = 25;
            UploadProgressBarValue = 0;

            // 统计全部待上传文件数量
            UploadProgressBarMax =
                Files.Count +
                Folder.Sum(folder =>
                    Directory.EnumerateFiles(
                        folder,
                        "*",
                        SearchOption.AllDirectories).Count());

            // 单独拖入的文件上传到当前服务器目录
            if (Files.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(
                    new FileTmMessage(
                        1,
                        Files,
                        _userInfoService.CurrentDirectoryId,_userInfoService.CurrentDirectoryPath));
            }

            // 每一个Folder都是顶级目录
            foreach (string folder in Folder)
            {
                await UploadFolderRecursiveAsync(
                    folder,
                    _userInfoService.CurrentDirectoryId,_userInfoService.CurrentDirectoryPath);
            }

            // 只有空文件夹，没有文件
            if (UploadProgressBarMax == 0)
            {
                ProgressHeight = 0;
            }
        }
        catch (Exception exception)
        {
            ProgressHeight = 0;

            Home.GlobalToastManager?.Show(
                new Toast($"添加上传任务失败：{exception.Message}"),
                type: NotificationType.Error);
        }
    }

    /// <summary>
    /// 文件夹递归
    /// </summary>
    /// <param name="localFolderPath">当前文件夹</param>
    /// <param name="serverParentFolderId">文件夹父id</param>
    private async Task UploadFolderRecursiveAsync(
        string localFolderPath,
        string serverParentFolderId,
        string serverParentFolderPath)
    {
        var directoryInfo = new DirectoryInfo(localFolderPath);

        if (!directoryInfo.Exists)
        {
            return;
        }

        // 创建当前服务器文件夹
        DefaultMsg re =
            await _webApiService.FileApi.CreateFolderAsync(
                serverParentFolderId,
                directoryInfo.Name);

        if (re.Status != 0)
        {
            return;
        }

        string? currentServerFolderId = re.Data?.ToString();

        if (string.IsNullOrWhiteSpace(currentServerFolderId))
        {
            return;
        }

        // 拼接当前文件夹的服务器路径
        string currentServerFolderPath =
            CombineCloudPath(
                serverParentFolderPath,
                directoryInfo.Name);

        // 只获取当前层文件
        List<string> currentFiles =
            Directory.EnumerateFiles(
                localFolderPath,
                "*",
                SearchOption.TopDirectoryOnly).ToList();

        if (currentFiles.Count > 0)
        {
            WeakReferenceMessenger.Default.Send(
                new FileTmMessage(
                    1,
                    currentFiles,
                    currentServerFolderId,
                    currentServerFolderPath));
        }

        // 将当前层的 ID 和路径继续传给子目录
        foreach (string childFolder in
                 Directory.EnumerateDirectories(
                     localFolderPath,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            await UploadFolderRecursiveAsync(
                childFolder,
                currentServerFolderId,
                currentServerFolderPath);
        }
    }
    private static string CombineCloudPath(
        string parentPath,
        string folderName)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return folderName;
        }

        string normalizedParent =
            parentPath.Replace('\\', '/');

        bool isRoot = normalizedParent == "/";

        normalizedParent =
            normalizedParent.TrimEnd('/');

        return isRoot
            ? $"/{folderName}"
            : $"{normalizedParent}/{folderName}";
    }
    /// <summary>
    /// 选择文件
    /// </summary>
    /// <param name="w"></param>
    [RelayCommand]
    private async Task SelectFilesAsync(Window w)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(w);

        if (topLevel?.StorageProvider is null)
        {
          return;
        }

        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择需要上传的文件",
                    AllowMultiple = true,

                    FileTypeFilter =
                    [
                        new FilePickerFileType("所有文件")
                        {
                            Patterns = ["*"]
                        },

                        new FilePickerFileType("图片")
                        {
                            Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                        }
                    ]
                });

        UploadFiles(files .Select(file => file.Path.LocalPath).ToList(),new List<string>());
    }
    /// <summary>
    /// 选择文件夹
    /// </summary>
    [RelayCommand]
    private async Task SelectFoldersAsync(Window w)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(w);

        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders =
            await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择需要上传的文件夹",
                    AllowMultiple = true
                });
        UploadFiles(new List<string>(),folders
            .Select(folder => folder.Path.LocalPath)
            .ToList());
       
    }

    /// <summary>
    /// 快速搜索item点击
    /// </summary>
    /// <param name="w"></param>
    [RelayCommand]
    private async Task SearchItemClick(FileSearchSimpleItem item)
    {
        WeakReferenceMessenger.Default.Send(new FilePageMessage(item.Path));
        Home.GlobalToastManager?.Show(
            new Toast($"已跳转到文件所在文件夹"),
            type: NotificationType.Success
        );
    }
}
