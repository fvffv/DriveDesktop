using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Ursa.Controls;
using Path = Avalonia.Controls.Shapes.Path;

namespace drive_desktop.ViewModels;

public partial class FilePageViewModel : ViewModelBase
{
    [ObservableProperty] private ObservableCollection<BreadcrumbNode> _breadcrumbList = new();

    /// <summary>
    /// 用于让面包屑的输入框失去焦点
    /// </summary>
    [ObservableProperty] private bool breadcrumbTextBoxEnabled = true;

    /// <summary>
    /// 文件信息类
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFileEmpty))]
    [NotifyPropertyChangedFor(nameof(IsConditionMet))]
    private ObservableCollection<UserFilesInfoItem> _fileInfos;
    public bool IsFileEmpty => FileInfos?.Count == 0;
    /// <summary>
    /// 文件总数 用于分页显示总数
    /// </summary>
    [ObservableProperty] 
    private int _fileCount = 0;
    /// <summary>
    /// 文件夹信息类
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFolderEmpty))]
    [NotifyPropertyChangedFor(nameof(IsConditionMet))]
    private ObservableCollection<UserDirsInfoItem> _folderInfos;

    public bool IsFolderEmpty => FolderInfos?.Count == 0;

    public bool IsConditionMet => !IsLoading && IsFileEmpty && IsFolderEmpty;
    /// <summary>
    /// 文件是否全选
    /// </summary>
    [ObservableProperty] private bool fileIsAllChecked = false;

    /// <summary>
    /// 文件夹是否全选
    /// </summary>
    [ObservableProperty] private bool folderIsAllChecked = false;

    /// <summary>
    /// 面包屑的路径
    /// </summary>
    [ObservableProperty] private string _breadcrumbPath = "";

    
    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly AppConfigService _appConfigService;
    public TopBarViewModel TopBarViewModel { get; set; }
    private readonly ITopLevelProvider _topLevelProvider;
    /// <summary>
    /// 是否正在加载中
    /// </summary>
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsConditionMet))]
    private bool _isLoading = true;

    //虚拟的占位数据，用于生成 12个骨架屏卡片
    public int[] SkeletonItems { get; } = new int[12];

    /// <summary>
    /// 用于加载下一页
    /// </summary>
    [ObservableProperty]
    private bool _isTurnPage = false;
    private int pageNumber = 1;
    // 每次切换/刷新目录都会递增。旧的分页请求返回后，版本不匹配就不能再写入当前列表。
    private int _directoryRequestVersion;

    private int BeginDirectoryRequest()
    {
        IsTurnPage = false;
        return ++_directoryRequestVersion;
    }

    private bool IsCurrentDirectoryRequest(int requestVersion) =>
        requestVersion == _directoryRequestVersion;

    public FilePageViewModel()
    {
      
    }

    public FilePageViewModel( UserInfoService userInfoService, WebApiService webApiService,AppConfigService appConfigService,TopBarViewModel topBarViewModel,ITopLevelProvider topLevelProvider)
    {
        _userInfoService = userInfoService;
        _webApiService = webApiService;
        _appConfigService = appConfigService;
        TopBarViewModel = topBarViewModel;
        _topLevelProvider = topLevelProvider;
        
        WeakReferenceMessenger.Default.Register<DefaultMsg>(this, async (r, m) =>
        {
            if (m.Msg == "TopBarDeleteActionMessage")
            {
                DelFileAndFolder();
            }
        });
        
        WeakReferenceMessenger.Default.Register<FilePageMessage>(this, async (r, m) =>
        {
            if (m.Path=="/")
            {
                TaskInitFileInfo();
            }
            else
            {
                BreadcrumbPath = m.Path;
                BreadcrumbInput();
            }
         
            
        });
        
        
        //文件夹快捷方式消费者
          WeakReferenceMessenger.Default.Register<SidebarItemMessage>(this,
           async  (recipient, message) =>
            {
                if (message.parameter is CustomView v)
                {
                    if (v.Type==1)
                    {
                        BreadcrumbPath = v.Keywords[0];
                        BreadcrumbInput();
                    }
                }
                
        
        });
    }

    public Task InitializeAsync()
    {
        return TaskInitFileInfo();
    }

    private async Task TaskInitFileInfo()
    {
        var requestVersion = BeginDirectoryRequest();
        IsLoading = true;
        BreadcrumbList.Clear();
        _userInfoService.CurrentDirectoryId = _userInfoService.ShowUserInfo.RootFolderId;
        _userInfoService.CurrentDirectoryPath = "/";
        var info =
            await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(_userInfoService.ShowUserInfo.RootFolderId);
        if (!IsCurrentDirectoryRequest(requestVersion))
        {
            return;
        }
        BreadcrumbList.Add(new BreadcrumbNode("首页", _userInfoService.ShowUserInfo.RootFolderId));
        if (info.Status == 0)
        {
            FileCount = info.Data.TotalFileCount;
            FileInfos = new ObservableCollection<UserFilesInfoItem>(info.Data.FileInfos);
            FileInfos.Where(x=>x.FileTypeInfo.IsImg).Mutate(x=>x.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{x.FileHash}.jpg");
            FolderInfos =
                new ObservableCollection<UserDirsInfoItem>(info.Data.Dirs.Where(x =>
                    x.Id != _userInfoService.ShowUserInfo.RootFolderId));
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取文件错误:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        IsTurnPage = false;
        IsLoading = false;
    }


    /// <summary>
    /// 文件全选/取消命令
    /// </summary>
    /// <param name="selectAll"></param>
    [RelayCommand]
    private void FileSelectAll(bool selectAll)
    {
        FileInfos.Mutate(x => x.IsChecked = selectAll);
        //若有勾选 显示批量操作按钮

        TopBarViewModel.IsShowBorder = FileInfos.Any(x => x.IsChecked) || FolderInfos.Any(x => x.IsChecked);
        TopBarViewModel.SelectedNum = FileInfos.Count(x => x.IsChecked) + FolderInfos.Count(x => x.IsChecked);
    }

    /// <summary>
    /// 文件夹全选/取消命令
    /// </summary>
    /// <param name="selectAll"></param>
    [RelayCommand]
    private void FolderSelectAll(bool selectAll)
    {
        FolderInfos.Mutate(x => x.IsChecked = selectAll);
        //若有勾选 显示批量操作按钮
        TopBarViewModel.IsShowBorder = FileInfos.Any(x => x.IsChecked) || FolderInfos.Any(x => x.IsChecked);
        TopBarViewModel.SelectedNum = FileInfos.Count(x => x.IsChecked) + FolderInfos.Count(x => x.IsChecked);
    }

    /// <summary>
    /// 文件单个item选择取消检查命令
    /// </summary>
    [RelayCommand]
    private void FileItemCheck()
    {
        FileIsAllChecked = FileInfos.Count(x => x.IsChecked) == FileInfos.Count;
        
        //若有勾选 显示批量操作按钮
        TopBarViewModel.IsShowBorder = FileInfos.Any(x => x.IsChecked) || FolderInfos.Any(x => x.IsChecked);
        TopBarViewModel.SelectedNum = FileInfos.Count(x => x.IsChecked) + FolderInfos.Count(x => x.IsChecked);
    }

    /// <summary>
    /// 文件夹单个item选择取消检查命令
    /// </summary>
    [RelayCommand]
    private void FolderItemCheck()
    {
        FolderIsAllChecked = FolderInfos.Count(x => x.IsChecked) == FolderInfos.Count;
        //若有勾选 显示批量操作按钮
        TopBarViewModel.IsShowBorder = FileInfos.Any(x => x.IsChecked) || FolderInfos.Any(x => x.IsChecked);
        TopBarViewModel.SelectedNum = FileInfos.Count(x => x.IsChecked) + FolderInfos.Count(x => x.IsChecked);
    }

    /// <summary>
    /// 文件夹被点击
    /// </summary>
    [RelayCommand]
    private async Task FolderClick(UserDirsInfoItem item)
    {
        var requestVersion = BeginDirectoryRequest();
        IsLoading = true; 
        pageNumber = 1;
        TopBarViewModel.IsShowBorder = false;
       
        var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(item.Id);
        if (!IsCurrentDirectoryRequest(requestVersion))
        {
            return;
        }
        if (info.Status == 0)
        {
            FileCount = info.Data.TotalFileCount;
            FolderIsAllChecked = false;
            FileIsAllChecked = false;
            BreadcrumbList.Add(new BreadcrumbNode(item.FolderName, item.Id));
            BreadcrumbPath = string.Join("/",
                BreadcrumbList.Where(x => x.FolderId != _userInfoService.ShowUserInfo.RootFolderId)
                    .Select(x => x.FolderName));
            FileInfos = new ObservableCollection<UserFilesInfoItem>(info.Data.FileInfos);
            FileInfos.Where(x=>x.FileTypeInfo.IsImg).Mutate(x=>x.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{x.FileHash}.jpg");
            FolderInfos = new ObservableCollection<UserDirsInfoItem>(info.Data.Dirs);
            
            _userInfoService.CurrentDirectoryId = item.Id;
            _userInfoService.CurrentDirectoryPath = BreadcrumbPath;
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"打开文件夹失败:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        IsTurnPage = false;
        IsLoading = false; 
        GC.Collect();
    }

    /// <summary>
    /// 面包屑点击事件
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task BreadcrumbClick(BreadcrumbNode item)
    {
        var requestVersion = BeginDirectoryRequest();
        
        //防止folderID是空
        if (string.IsNullOrEmpty(item.FolderId))
        {
            item.FolderId = (await _webApiService.FileApi.GetFolderByPathStrictAsync(_userInfoService.ShowUserInfo.RootFolderId,
                BreadcrumbPath)).Data.ToString();
            if (!IsCurrentDirectoryRequest(requestVersion))
            {
                return;
            }
            if (string.IsNullOrEmpty(item.FolderId))
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"当前文件夹不存在"),
                    type: NotificationType.Error
                );
                return;
            }
        }
        IsLoading = true; 
        var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(item.FolderId);
        if (!IsCurrentDirectoryRequest(requestVersion))
        {
            return;
        }
        if (info.Status == 0)
        {
            pageNumber = 1;
            TopBarViewModel.IsShowBorder = false;
            FileCount = info.Data.TotalFileCount;
            FolderIsAllChecked = false;
            FileIsAllChecked = false;
            //删除面包屑当前点击的后面的节点
            int index = BreadcrumbList.IndexOf(item);
            int count = BreadcrumbList.Count - 1 - index;
            for (int i = 0; i < count; i++)
            {
                BreadcrumbList.RemoveAt(index + 1);
            }

            BreadcrumbPath = string.Join("/",
                BreadcrumbList.Where(x => x.FolderId != _userInfoService.ShowUserInfo.RootFolderId)
                    .Select(x => x.FolderName));
            FileInfos = new ObservableCollection<UserFilesInfoItem>(info.Data.FileInfos);
            FileInfos.Where(x=>x.FileTypeInfo.IsImg).Mutate(x=>x.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{x.FileHash}.jpg");
            FolderInfos = new ObservableCollection<UserDirsInfoItem>(info.Data.Dirs.Where(x =>
                x.Id != _userInfoService.ShowUserInfo.RootFolderId));
            _userInfoService.CurrentDirectoryId = item.FolderId;
            _userInfoService.CurrentDirectoryPath = BreadcrumbPath;
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"打开文件夹失败:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        IsTurnPage = false;
        IsLoading = false; 
        GC.Collect();
    }

    [RelayCommand]
    private async Task BreadcrumbInput()
    {
        var requestVersion = BeginDirectoryRequest();
        if (string.IsNullOrWhiteSpace(BreadcrumbPath))
        {
            return;
        }
        
        var info = await _webApiService.FileApi.GetFolderByPathStrictAsync(_userInfoService.ShowUserInfo.RootFolderId,
            BreadcrumbPath);
        if (!IsCurrentDirectoryRequest(requestVersion))
        {
            return;
        }
        if (info.Status == 0)
        {
            IsLoading = true; 
            pageNumber = 1;
            TopBarViewModel.IsShowBorder = false;
            BreadcrumbTextBoxEnabled = false;
            BreadcrumbTextBoxEnabled = true;
            var info2 = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(info.Data.ToString());
            if (!IsCurrentDirectoryRequest(requestVersion))
            {
                return;
            }
            FileCount = info2.Data.TotalFileCount;
            FileInfos = new ObservableCollection<UserFilesInfoItem>(info2.Data.FileInfos);
            FileInfos.Where(x=>x.FileTypeInfo.IsImg).Mutate(x=>x.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{x.FileHash}.jpg");
            FolderInfos = new ObservableCollection<UserDirsInfoItem>(info2.Data.Dirs.Where(x =>
                x.Id != _userInfoService.ShowUserInfo.RootFolderId));
            //重新构造面包屑
            BreadcrumbList = new ObservableCollection<BreadcrumbNode>(
                // 先创建一个包含首页的数组
                new[] { new BreadcrumbNode("首页", _userInfoService.ShowUserInfo.RootFolderId) }
                    // 切割后的路径过滤空值，并映射拼接
                    .Concat(BreadcrumbPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => new BreadcrumbNode(item, null)))
            );
            
            _userInfoService.CurrentDirectoryId = info.Data.ToString();
            _userInfoService.CurrentDirectoryPath = BreadcrumbPath;
          
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"目录错误:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        IsTurnPage = false;
        IsLoading = false; 
        GC.Collect();
    }

    /// <summary>
    /// 刷新当前目录
    /// </summary>
    [RelayCommand]
    private async Task RefreshCurrentDirectory()
    {
        var requestVersion = BeginDirectoryRequest();
        IsLoading = true;
        pageNumber = 1;
        var info =
            await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(_userInfoService.CurrentDirectoryId);
        if (!IsCurrentDirectoryRequest(requestVersion))
        {
            return;
        }
        if (info.Status == 0)
        {
            FileIsAllChecked = false;
            FolderIsAllChecked = false;
            
            TopBarViewModel.IsShowBorder = false;
            FileCount = info.Data.TotalFileCount;
            FileInfos = new ObservableCollection<UserFilesInfoItem>(info.Data.FileInfos);
            //检测有没有图片 有的话加载缩略图
            FileInfos.Where(x=>x.FileTypeInfo.IsImg).Mutate(x=>x.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{x.FileHash}.jpg");
            FolderInfos =
                new ObservableCollection<UserDirsInfoItem>(info.Data.Dirs.Where(x =>
                    x.Id != _userInfoService.ShowUserInfo.RootFolderId));
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取文件错误:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        IsTurnPage = false;
        IsLoading = false;
    }
    /// <summary>
    /// 翻页
    /// </summary>
    [RelayCommand]
    private async Task LoadNextPage()
    {
        // 目录切换时禁止旧列表继续触发分页请求。
        if (IsLoading || IsTurnPage || FileInfos == null)
        {
            return;
        }
        if (FileInfos.Count == FileCount)
        {
            return;
        }
        
        var requestVersion = _directoryRequestVersion;
        var directoryId = _userInfoService.CurrentDirectoryId;
        var targetFileInfos = FileInfos;
        var nextPageNumber = pageNumber + 1;
        IsTurnPage = true;
        try
        {
            var info = await _webApiService.FileApi.GetUserDirectoryFileInfoAsync(directoryId, nextPageNumber);
            if (!IsCurrentDirectoryRequest(requestVersion) ||
                !string.Equals(directoryId, _userInfoService.CurrentDirectoryId, StringComparison.Ordinal) ||
                !ReferenceEquals(targetFileInfos, FileInfos))
            {
                return;
            }

            if (info.Status == 0)
            {
                foreach (var item in info.Data.FileInfos)
                {
                    if (item.FileTypeInfo.IsImg)
                    {
                        item.ImageUrl = $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{item.FileHash}.jpg";
                    }
                    targetFileInfos.Add(item);
                }
                pageNumber = nextPageNumber;
            }
            else
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"获取文件错误:{info.Msg}"),
                    type: NotificationType.Error
                );
            }
        }
        finally
        {
            if (IsCurrentDirectoryRequest(requestVersion) &&
                ReferenceEquals(targetFileInfos, FileInfos))
            {
                IsTurnPage = false;
            }
        }
    }
    /// <summary>
    /// 编辑文件或文件夹名字
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void EditName(object item)
    {
     
        WeakReferenceMessenger.Default.Send(new DialogMessage("NewFolderDialog", true,new NewFolderMessage(item)));
       
    }

    /// <summary>
    /// 删除文件或文件夹名字
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task DelFileOrFolder(object item)
    {
        DefaultMsg info = null;
            if (item is UserFilesInfoItem fi)
            {
                
                MessageBoxResult Result = await OverlayMessageBox.ShowAsync($"是否删除当前文件:{fi.FileName}", "删除确认", icon:MessageBoxIcon.Warning, button:MessageBoxButton.OKCancel);
                if (Result == MessageBoxResult.OK)
                {
                    info =await _webApiService.FileApi.DeleteUserFileAsync(new []{fi.Id});
                }
                else
                {
                    return;
                }
                
            }
            if (item is UserDirsInfoItem di)
            {
                MessageBoxResult Result = await OverlayMessageBox.ShowAsync($"是否删除当前文件夹:{di.FolderName}", "删除确认", icon:MessageBoxIcon.Warning, button:MessageBoxButton.OKCancel);
                if (Result == MessageBoxResult.OK)
                {
                    info =await  _webApiService.FileApi.DeleteUserFolderAsync(new []{di.Id});
                }
                else
                {
                    return;
                }
                
            }

            if (info.Status == 0)
            {
                RefreshCurrentDirectory();
            }
            else
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"删除错误:{info.Msg}"),
                    type: NotificationType.Error
                );
            }
      
    }
    
    [RelayCommand]
    private async Task CopyFileLink(UserFilesInfoItem item)
    {
        await _topLevelProvider.GetTopLevel()?.Clipboard.SetTextAsync($"{_appConfigService.Config.ServerIp}/api/Files/DirectLink/{item.Id}");
        Home.GlobalToastManager?.Show(
            new Toast("复制成功"),
            type: NotificationType.Success
        );
    }
    
    /// <summary>
    /// 执行批量删除文件和文件夹
    /// </summary>
    private async Task DelFileAndFolder()
    {
        
        if (await OverlayMessageBox.ShowAsync("是否删除所选文件/文件夹", "批量删除确认", icon: MessageBoxIcon.Warning, button: MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;

        var fileIds = FileInfos.Where(x => x.IsChecked).Select(x => x.Id).ToArray();
        var folderIds = FolderInfos.Where(x => x.IsChecked).Select(x => x.Id).ToArray();

      
        void ShowToast(int status, string msg, string typeName)
        {
            bool isSuccess = status == 0;
            Home.GlobalToastManager?.Show(
                new Toast($"{typeName}批量删除{(isSuccess ? "成功" : $"失败:{msg}")}"), 
                type: isSuccess ? NotificationType.Success : NotificationType.Error);
        }

        //执行删除逻辑
        if (fileIds.Length > 0)
        {
            var res = await _webApiService.FileApi.DeleteUserFileAsync(fileIds);
            ShowToast(res.Status, res.Msg, "文件");
        }

        if (folderIds.Length > 0) // 修复了原代码这里写成 reFiles 的Bug
        {
            var res = await _webApiService.FileApi.DeleteUserFolderAsync(folderIds);
            ShowToast(res.Status, res.Msg, "文件夹");
        }

        TopBarViewModel.IsShowBorder = false;
        RefreshCurrentDirectory();
    }
    
    /// <summary>
    /// 打开属性弹窗
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void OpenFileProperties(object item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("FilePropertiesDialog", true,item));
    }
    /// <summary>
    /// 打开文件
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task OpenFile(UserFilesInfoItem item)
    {
        if (item.FileTypeInfo.TypeName is "图片")
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("ImageViewDialog", true,item));
        }
        
        if (System.IO.Path.GetExtension(item.FileName)  is ".md"  )
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("TextViewDialog", true,new TextViewMessage(1,item)));
        }
        if (item.FileTypeInfo.TypeName is "代码"  )
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("TextViewDialog", true,new TextViewMessage(0,item)));
        }
        
        if (item.FileTypeInfo.TypeName is "图片")
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("ImageViewDialog", true,item));
        }
        if (item.FileTypeInfo.TypeName is "文档2007")
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("DocumentViewDialog", true,item));
        }
        if (item.FileTypeInfo.TypeName is "音频")
        {
            WeakReferenceMessenger.Default.Send(new MusicPlayMsg(true, item));
        }
        if (item.FileTypeInfo.TypeName is "视频")
        {
            var info =await _webApiService.FileApi.GetFileDownLoadTempKeyAsync(item.Id);
            if (info.Status == 0)
            {
                new VideoPreviewView()
                {
                    DataContext = new VideoPreviewViewModel( $"{_appConfigService.Config.ServerIp.TrimEnd('/')}/api/Files/DownLoadKey/{info.Data}",item.FileName)
                }.Show();
            }
            else
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"获取文件[{item.FileName}]失败: {info.Msg}"), 
                    type: NotificationType.Error);
            }
            
        }
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task OpenShareFile(UserFilesInfoItem item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("ShareFileDialog", true,item));
    }


    [RelayCommand]
    private async Task DownloadFile(UserFilesInfoItem item)
    {
        WeakReferenceMessenger.Default.Send(new FileTmMessage(0,item));
     
    }
    
    
}
