using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

/// <summary>
/// 搜索页vm
/// </summary>
public partial class SearchPageViewModel : ViewModelBase
{
    private const string FolderCategoryName = "文件夹";
    /// <summary>
    /// 文件信息总类
    /// </summary>
    [ObservableProperty] private ObservableCollection<object> _fileSystemItems = new();
    /// <summary>
    /// 搜索返回初始数据
    /// </summary>
    [ObservableProperty] private ObservableCollection<object> _fileSystemItemsInit = new();
    /// <summary>
    /// 二次分类
    /// </summary>
    public ObservableCollection<string> Secondary { get; } = new();
    
    /// <summary>
    /// 二次过滤
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
   /// <summary>
    /// 二次过滤
    /// </summary>
    private void Secondary_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplySecondaryFilter();
    }
    
    
    /// <summary>
    /// 根据 Secondary 中选中的二次分类，对搜索结果进行再次过滤。
    /// FileSystemItemsInit 保存的是接口返回的原始完整结果，
    /// FileSystemItems 保存的是当前界面真正显示的结果。
    /// </summary>
    private void ApplySecondaryFilter()
    {
        // 没有选择任何二次分类时，直接恢复显示全部原始搜索结果
        if (Secondary.Count == 0)
        {
            FileSystemItems = new ObservableCollection<object>(FileSystemItemsInit);
            return;
        }

        // 将当前选中的分类转成 HashSet，提高 Contains 判断效率
        // 例如：["图片", "文档", "文件夹"]
        var selected = Secondary.ToHashSet(StringComparer.Ordinal);

        // 从原始结果 FileSystemItemsInit 中筛选出符合分类条件的项目
        var filtered = FileSystemItemsInit.Where(item => item switch
        {
            // 如果是文件，则根据文件类型名称判断是否命中二次分类
            // 例如 fi.FileTypeInfo.TypeName == "图片"
            UserFilesInfoItem fi => fi.FileTypeInfo != null &&
                                    selected.Contains(fi.FileTypeInfo.TypeName),

            // 如果是文件夹，则统一按 FolderCategoryName 判断
            UserDirsInfoItem => selected.Contains(FolderCategoryName),

            // 其他未知类型不显示
            _ => false
        });

        // 用筛选后的结果替换当前界面显示集合
        FileSystemItems = new ObservableCollection<object>(filtered);
    }
    /// <summary>
    /// 是否搜索完成
    /// </summary>
    [ObservableProperty] private bool _isLoaded = true;

    /// <summary>
    /// 是否启用精细搜索
    /// </summary>
    [ObservableProperty] private bool _isAdvancedSearch;

    [ObservableProperty] private SearchInfo _searchInfo = new SearchInfo();

    /// <summary>
    /// 快速搜索标签
    /// </summary>
    [ObservableProperty] private bool _fastDoc = false;

    [ObservableProperty] private bool _fastImg = false;
    [ObservableProperty] private bool _fastZip = false;
    [ObservableProperty] private bool _fastDay7 = false;

    private readonly WebApiService _webApiService;
    private readonly AppConfigService _appConfigService;
    private readonly ITopLevelProvider _topLevelProvider;

    public SearchPageViewModel()
    {
    }

    public SearchPageViewModel(ITopLevelProvider topLevelProvider, AppConfigService appConfigService,
        WebApiService webApiService)
    {
        Secondary.CollectionChanged += Secondary_CollectionChanged;
        _appConfigService = appConfigService;
        _webApiService = webApiService;
        _topLevelProvider = topLevelProvider;
        
        //收到自定义视图type=0消息
        WeakReferenceMessenger.Default.Register<SidebarItemMessage>(this,
           async  (recipient, message) =>
            {
                if (message.parameter is CustomView v)
                {
                    if (v.Type==0)
                    {
                        IsLoaded = false;
                        IsAdvancedSearch = false;
                        SearchInfo sf = new SearchInfo();
                        sf.Keyword = string.Join(',',v.Keywords) ;

                        if (string.IsNullOrEmpty(sf.Keyword) )
                        {
                            IsLoaded = true;
                            return;
                        }
                        //请求数据
                        var info = await _webApiService.FileApi.SearchFilesAsync(sf,false);

                        
                        if (info.Status == 0)
                        {
                            //处理数据
                            FileSystemItemsInit.Clear();
                            Secondary.Clear(); 
                            //处理文件夹
                            if (info.Data?.Dirs != null)
                            {
                                foreach (var item in info.Data.Dirs)
                                {
                                    FileSystemItemsInit.Add(item);
                                }
                            }

                            //处理文件
                            if (info.Data?.FileInfos != null)
                            {
                                foreach (var item in info.Data.FileInfos)
                                {
                                    if (item.FileTypeInfo.IsImg)
                                    {
                                        item.ImageUrl =
                                            $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{item.FileHash}.jpg";
                                    }

                                    FileSystemItemsInit.Add(item);
                                }
                            }
                
                            ApplySecondaryFilter();
                        }
                        else
                        {
                            Home.GlobalToastManager?.Show(
                                new Toast($"自定义视图失败:{info.Msg}"),
                                type: NotificationType.Error
                            );
                        }
                        IsLoaded = true;
                    }
                }
                
        
            });
        
      
    }

    /// <summary>
    /// 编辑文件或文件夹名字
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void EditName(object item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("NewFolderDialog", true, new NewFolderMessage(item)));
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
            MessageBoxResult Result = await OverlayMessageBox.ShowAsync($"是否删除当前文件:{fi.FileName}", "删除确认",
                icon: MessageBoxIcon.Warning, button: MessageBoxButton.OKCancel);
            if (Result == MessageBoxResult.OK)
            {
                info = await _webApiService.FileApi.DeleteUserFileAsync(new[] { fi.Id });
            }
            else
            {
                return;
            }
        }

        if (item is UserDirsInfoItem di)
        {
            MessageBoxResult Result = await OverlayMessageBox.ShowAsync($"是否删除当前文件夹:{di.FolderName}", "删除确认",
                icon: MessageBoxIcon.Warning, button: MessageBoxButton.OKCancel);
            if (Result == MessageBoxResult.OK)
            {
                info = await _webApiService.FileApi.DeleteUserFolderAsync(new[] { di.Id });
            }
            else
            {
                return;
            }
        }

        if (info.Status == 0)
        {
            FileSystemItems.Remove(info);
            Home.GlobalToastManager?.Show(
                new Toast($"删除成功"),
                type: NotificationType.Success
            );
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"删除错误:{info.Msg}"),
                type: NotificationType.Error
            );
        }
    }

    /// <summary>
    /// 复制直连
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task CopyFileLink(UserFilesInfoItem item)
    {
        await _topLevelProvider.GetTopLevel()?.Clipboard
            .SetTextAsync($"{_appConfigService.Config.ServerIp}/api/Files/DirectLink/{item.Id}");
        Home.GlobalToastManager?.Show(
            new Toast("复制成功"),
            type: NotificationType.Success
        );
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task OpenShareFile(UserFilesInfoItem item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("ShareFileDialog", true, item));
    }

    /// <summary>
    /// 打开属性弹窗
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private void OpenFileProperties(object item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("FilePropertiesDialog", true, item));
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
            WeakReferenceMessenger.Default.Send(new DialogMessage("ImageViewDialog", true, item));
        }

     
        if (System.IO.Path.GetExtension(item.FileName) is ".md")
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("TextViewDialog", true,
                new TextViewMessage(1, item)));
        }

        if (item.FileTypeInfo.TypeName is "代码")
        {
            WeakReferenceMessenger.Default.Send(new DialogMessage("TextViewDialog", true,
                new TextViewMessage(0, item)));
        }
    }


    /// <summary>
    /// 搜索
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task SearchFile()
    {
        DefaultMsg<UserFilesInfo> info = null;
        IsLoaded = false;
        if (!IsAdvancedSearch)
        {
            SearchInfo sf = new SearchInfo();
            sf.Keyword = SearchInfo.Keyword;
            if (FastDoc)
            {
                sf.FileType.Add("文档");
            }

            if (FastImg)
            {
                sf.FileType.Add("图片");
            }

            if (FastZip)
            {
                sf.FileType.Add("压缩包");
            }

            if (FastDay7)
            {
                sf.StarLastModifiedTime = DateTime.Now.AddDays(-7);
            }
            //空值拦截
            if (string.IsNullOrEmpty(SearchInfo.Keyword) && sf.FileType.Count == 0 && sf.EndCreationTime == null &&
                sf.FileSizeInBytesMax == null && sf.FileSizeInBytesMin == null && sf.StarCreationTime == null &&
                sf.StarLastModifiedTime == null && sf.EndLastModifiedTime == null)
            {
                Home.GlobalToastManager?.Show(
                    new Toast("不能所有条件都是空哦"),
                    type: NotificationType.Error
                );
                IsLoaded = true;
                return;
            }

            info = await _webApiService.FileApi.SearchFilesAsync(sf);
        }
        else
        {
            //空值拦截
            if (string.IsNullOrEmpty(SearchInfo.Keyword) && SearchInfo.FileType.Count == 0 && SearchInfo.EndCreationTime == null &&
                SearchInfo.FileSizeInBytesMax == null && SearchInfo.FileSizeInBytesMin == null && SearchInfo.StarCreationTime == null &&
                SearchInfo.StarLastModifiedTime == null && SearchInfo.EndLastModifiedTime == null)
            {
                Home.GlobalToastManager?.Show(
                    new Toast("不能所有条件都是空哦"),
                    type: NotificationType.Error
                );  
                IsLoaded = true;
                return;
            }

            info = await _webApiService.FileApi.SearchFilesAsync(SearchInfo);
        }

        if (info.Status == 0)
        {
            FileSystemItemsInit.Clear();
            //处理文件夹
            if (info.Data?.Dirs != null)
            {
                foreach (var item in info.Data.Dirs)
                {
                    FileSystemItemsInit.Add(item);
                }
            }

            //处理文件
            if (info.Data?.FileInfos != null)
            {
                foreach (var item in info.Data.FileInfos)
                {
                    if (item.FileTypeInfo.IsImg)
                    {
                        item.ImageUrl =
                            $"{_appConfigService.Config.ServerIp}/driveassets/imgcomp/{item.FileHash}.jpg";
                    }

                    FileSystemItemsInit.Add(item);
                }
            }

            ApplySecondaryFilter();
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"搜索失败:{info.Msg}"),
                type: NotificationType.Error
            );
        }

        IsLoaded = true;
    }

    /// <summary>
    /// 打开文件或文件夹所在的文件夹
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task OpenFileOrFolderPFolder(object item)
    {
        DefaultMsg info = null;
        if (item is UserFilesInfoItem fi)
        {
             info = await _webApiService.FileApi.GetFullFolderPathAsync(fi.FolderId);
           
        }
        if (item is UserDirsInfoItem di)
        {
             info = await _webApiService.FileApi.GetFullFolderPathAsync(di.Id);
           
        }

        if (info.Status==0)
        {
            
            WeakReferenceMessenger.Default.Send(new FilePageMessage(info.Data.ToString()));
            WeakReferenceMessenger.Default.Send(new SidebarItemMessage(0, "我的文件"));
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"打开失败:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        
        
       
    }
    [RelayCommand]
    private async Task DownloadFile(UserFilesInfoItem item)
    {
        WeakReferenceMessenger.Default.Send(new FileTmMessage(0,item));
    }
}