using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class MoveFilesViewModel : ViewModelBase
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectNum))]
    private ObservableCollection<UserFiles> _folderOrFileInfos;

    /// <summary>
    /// 当前选中文件总数
    /// </summary>
    [ObservableProperty] private int _selectNum = 0;

    /// <summary>
    /// 当前目录文件夹展示
    /// </summary>
    [ObservableProperty] private ObservableCollection<UserDirsInfoItem> _moveFolderInfos;

    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;

    /// <summary>
    /// 是否在根目录
    /// </summary>
    [ObservableProperty] private bool _isRoot;

    /// <summary>
    /// 当前目录名称
    /// </summary>
    [ObservableProperty] private string _folderName;

    /// <summary>
    /// radio选中
    /// </summary>
    [ObservableProperty] private string _isChecked;

    /// <summary>
    /// 选中的item
    /// </summary>
    private UserDirsInfoItem? _selectedFolderInfo;

    [ObservableProperty] private ButtonStatus _buttonStatus = new("移动到 当前目录");

    /// <summary>
    /// 当前所在目录id
    /// </summary>
    private string LocalFolderId;

    //目录缓存
    private readonly Dictionary<string, List<UserDirsInfoItem>> _localFolderCache = new();

    //历史记录栈：记录进入的每一层目录的 (Id, 名称)
    private readonly Stack<(string FolderId, string FolderName)> _historyStack = new();

    public MoveFilesViewModel()
    {
        MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>
        {
            new UserDirsInfoItem
            {
                Id = "83f22c91-bc15-41e9-8f8b-e87dfeb6e778",
                FolderName = "/",
                CreationTime = DateTime.Parse("2026-05-06T23:06:20.061059")
            },
            new UserDirsInfoItem
            {
                Id = "63f569d6-393f-4ca7-814c-81407863c9c2",
                FolderName = "Cache1",
                CreationTime = DateTime.Parse("2026-05-16T15:37:21.763286")
            },
            new UserDirsInfoItem
            {
                Id = "69504ec3-420109-463a-b5c5-2759a813ea88",
                FolderName = "手机备",
                CreationTime = DateTime.Parse("2026-05-10T09:31:35.481867")
            },
            new UserDirsInfoItem
            {
                Id = "21d9cf19-4f15-4943-976e-4d2d481264b4",
                FolderName = "文件夹",
                CreationTime = DateTime.Parse("2026-05-20T14:40:56.728393")
            },
            new UserDirsInfoItem
            {
                Id = "a55fd92a-6bbe-472d-a70c-0b8e6d85ede0",
                FolderName = "Camera",
                CreationTime = DateTime.Parse("2026-05-07T14:05:00.609263")
            },
            new UserDirsInfoItem
            {
                Id = "202d2faf-c7ee-4455-8ee4-92014d644f64",
                FolderName = "测试",
                CreationTime = DateTime.Parse("2026-06-07T22:31:52.392029")
            }
        };
    }

    [ObservableProperty] private bool _isShowMoveFilesDialog = false;
    private readonly FilePageViewModel _filePageViewModel;
    private readonly TopBarViewModel _topBarViewModel;

    public MoveFilesViewModel(UserInfoService userInfoService, FilePageViewModel filePageViewModel,
        WebApiService webApiService, TopBarViewModel topBarViewModel)
    {
        _userInfoService = userInfoService;
        _webApiService = webApiService;
        _topBarViewModel = topBarViewModel;
        _filePageViewModel = filePageViewModel;
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "MoveFilesDialog")
                {
                    IsShowMoveFilesDialog = message.IsShow;
                }

                if (message.Name is "MoveFilesDialog")
                {
                    //获取用户【打勾选中】了哪些文件和文件夹
                    FolderOrFileInfos = new ObservableCollection<UserFiles>(_filePageViewModel.FileInfos.Where(x => x.IsChecked));
                    foreach (var item in  _filePageViewModel.FolderInfos.Where(x => x.IsChecked))
                    {
                        FolderOrFileInfos.Add(item);
                    }

                    _filePageViewModel.FolderInfos.Where(x => x.IsChecked);

                    //设定弹窗初始的目录环境
                    LocalFolderId = _userInfoService.CurrentDirectoryId;
                    FolderName = _filePageViewModel.BreadcrumbList.LastOrDefault()?.FolderName ?? "首页";
                    IsRoot = _userInfoService.CurrentDirectoryId == _userInfoService.ShowUserInfo.RootFolderId;
                    _selectedFolderInfo = new UserDirsInfoItem() { Id = _userInfoService.CurrentDirectoryId };
                    // 3. 清理上一轮弹窗残留的历史和缓存
                    _historyStack.Clear();
                    _localFolderCache.Clear();
                    //将主页面的面包屑预先填充进历史栈
                    //遍历除了最后一个节点以外的所有父级节点
                    if (_filePageViewModel.BreadcrumbList != null)
                    {
                        for (int i = 0; i < _filePageViewModel.BreadcrumbList.Count - 1; i++)
                        {
                            var node = _filePageViewModel.BreadcrumbList[i];
                            // 按照顺序压入栈，底层会在最下面，紧挨着的上级会在栈顶
                            _historyStack.Push((node.FolderId, node.FolderName));
                        }
                    }

                    //调用初始化方法，拉取目录并建立缓存
                    InitializeRootFolderAsync();
                }
            });
    }

    [RelayCommand]
    private void Close()
    {
        IsShowMoveFilesDialog = false;
    }

    /// <summary>
    /// 文件夹双击
    /// </summary>
    [RelayCommand]
    private async Task FoloderDouble(UserDirsInfoItem targetFolder)
    {
        //进栈
        _historyStack.Push((LocalFolderId, FolderName));

        //更新当前目录信息
        LocalFolderId = targetFolder.Id;
        FolderName = targetFolder.FolderName;
        _selectedFolderInfo = targetFolder;
        IsRoot = false;

        //缓存
        if (_localFolderCache.TryGetValue(LocalFolderId, out var cachedDirs))
        {
            MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>(cachedDirs);
            ButtonStatus.Title = $"移动到 当前目录";
            return;
        }


        var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(LocalFolderId);

        if (info.Status == 0)
        {
            // 滤
            var validDirs = info.Data.Dirs
                .Where(x => x.Id != _userInfoService.ShowUserInfo.RootFolderId)
                .ExceptBy(FolderOrFileInfos.Select(x => x.Id), x => x.Id)
                .ToList();

            //存入本地缓存
            _localFolderCache[LocalFolderId] = validDirs;
            MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>(validDirs);
            ButtonStatus.Title = $"移动到 当前目录";
        }
        else
        {
            var rollbackNode = _historyStack.Pop();
            LocalFolderId = rollbackNode.FolderId;
            FolderName = rollbackNode.FolderName;
            IsRoot = (_historyStack.Count == 0);

            Home.GlobalToastManager?.Show(new Toast($"目录打开失败:{info.Msg}"), type: NotificationType.Error);
        }
    }

    /// <summary>
    /// 确定移动
    /// </summary>
    [RelayCommand]
    private async Task Move()
    {
        ButtonStatus.IsLoading = true;
        ButtonStatus.IsEnabled = false;
        ButtonStatus.Title = "提交中";
        var info = await _webApiService.FileApi.MoveFileOrDirAsync(new FileOrDirMoveInfo()
        {
            NewFolderId = _selectedFolderInfo.Id, 
            FileIds = FolderOrFileInfos.Where(x=>x is UserFilesInfoItem).Select(x => x.Id).ToArray(),
            FolderIds = FolderOrFileInfos.Where(x=>x is UserDirsInfoItem).Select(x => x.Id).ToArray()
        });

        if (info.Status == 0)
        {
            ButtonStatus.IsLoading = false;
            ButtonStatus.IsEnabled = true;
            ButtonStatus.Title = "保存";
            Close();
            _filePageViewModel.RefreshCurrentDirectoryCommand.Execute(null);
            Home.GlobalToastManager?.Show(new Toast("移动成功"), type: NotificationType.Success);
            _topBarViewModel.IsShowBorder = false;
        }
        else
        {
            Home.GlobalToastManager?.Show(new Toast($"移动失败:{info.Msg}"), type: NotificationType.Error);
        }
    }

    /// <summary>
    /// 返回上级
    /// </summary>
    [RelayCommand]
    private async Task ReturnUpper()
    {
        // 如果已经没有历史记录，或者已经是根目录，直接返回
        if (_historyStack.Count == 0 || LocalFolderId == _userInfoService.ShowUserInfo.RootFolderId)
        {
            IsRoot = true;
            return;
        }

        // 出栈
        var parentNode = _historyStack.Pop();

        //更新信息
        LocalFolderId = parentNode.FolderId;
        FolderName = parentNode.FolderName;
        _selectedFolderInfo = new UserDirsInfoItem() { Id = LocalFolderId };
        //是不是回到了根目录
        IsRoot = (LocalFolderId == _userInfoService.ShowUserInfo.RootFolderId);

        //缓存
        if (_localFolderCache.TryGetValue(LocalFolderId, out var cachedDirs))
        {
            MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>(cachedDirs);
            if (ButtonStatus != null) ButtonStatus.Title = "移动到 当前目录";
            return;
        }

        //没有命中缓存
        if (ButtonStatus != null)
        {
            ButtonStatus.IsLoading = true;
            ButtonStatus.Title = "正在加载...";
        }

        var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(LocalFolderId);

        if (ButtonStatus != null) ButtonStatus.IsLoading = false;

        if (info.Status == 0)
        {
            //过滤掉
            var validDirs = info.Data.Dirs
                .Where(x => x.Id != _userInfoService.ShowUserInfo.RootFolderId)
                .ExceptBy(FolderOrFileInfos.Select(x => x.Id), x => x.Id)
                .ToList();

            // 存入缓存
            _localFolderCache[LocalFolderId] = validDirs;

            // 更新UI
            MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>(validDirs);
            if (ButtonStatus != null) ButtonStatus.Title = "移动到 当前目录";
        }
        else
        {
            _historyStack.Push((LocalFolderId, FolderName));
            Home.GlobalToastManager?.Show(new Toast($"上级目录拉取失败:{info.Msg}"), type: NotificationType.Error);
        }
    }

    /// <summary>
    /// 选中
    /// </summary>
    [RelayCommand]
    private void Selected(UserDirsInfoItem item)
    {
        _selectedFolderInfo = item;
        ButtonStatus.Title = $"移动到 {item.FolderName}";
    }

    public async Task InitializeRootFolderAsync()
    {
        if (ButtonStatus != null)
        {
            ButtonStatus.IsLoading = true;
            ButtonStatus.Title = "正在加载...";
        }

        //去服务器拉取当前目录下的所有文件/文件夹
        var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(LocalFolderId);

        if (ButtonStatus != null) ButtonStatus.IsLoading = false;

        if (info.Status == 0)
        {
            // 核心过滤逻辑：过滤掉根目录本身；把刚才用户【打勾选中】的文件夹剔除掉
            var validDirs = info.Data.Dirs
                .Where(x => x.Id != _userInfoService.ShowUserInfo.RootFolderId)
                .ExceptBy(FolderOrFileInfos.Select(x => x.Id), x => x.Id)
                .ToList();

            //把整理好的列表塞进本地字典，第一层缓存建立完毕！
            _localFolderCache[LocalFolderId] = validDirs;

            // 绑定给 UI 显示的目录列表
            MoveFolderInfos = new ObservableCollection<UserDirsInfoItem>(validDirs);

            if (ButtonStatus != null)
                ButtonStatus.Title = $"移动到 {(IsRoot ? "当前目录" : FolderName)}";
        }
        else
        {
            Home.GlobalToastManager?.Show(new Toast($"目录打开失败:{info.Msg}"), type: NotificationType.Error);
            if (ButtonStatus != null) ButtonStatus.Title = "加载失败";
        }
    }
}