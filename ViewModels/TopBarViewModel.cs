using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class TopBarViewModel : ViewModelBase
{
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _searchBorderDelayCts;
    [ObservableProperty] private string _title = "我的文件";
    [ObservableProperty] private Bitmap _headImg;
    [ObservableProperty] private int _selectedNum = 0;
    [ObservableProperty] private string _searchContent = string.Empty;
    /// <summary>
    /// 搜索框焦点
    /// </summary>
    [ObservableProperty] private bool _searchIsfocus;

    /// <summary>
    /// 搜索结果区域是否显示
    /// </summary>
    [ObservableProperty] private bool _searchBorderShow;

    partial void OnSearchIsfocusChanged(bool value)
    {
        _searchBorderDelayCts?.Cancel();

        if (value)
        {
            SearchBorderShow = true;
            return;
        }

        var cts = new CancellationTokenSource();
        _searchBorderDelayCts = cts;
        _ = HideSearchBorderAfterDelayAsync(cts.Token);
    }

    private async Task HideSearchBorderAfterDelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(0.2), cancellationToken);
        SearchBorderShow = false;
    }
    
    
    /// <summary>
    /// 延迟防抖1秒执行
    /// </summary>
    /// <param name="oldValue"></param>
    /// <param name="newValue"></param>
    partial void OnSearchContentChanged(string? oldValue, string newValue)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;

        _ = ExecuteSearchAfterDelayAsync(newValue, cts.Token);
    }

    private async Task ExecuteSearchAfterDelayAsync(
        string searchContent,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            if (string.IsNullOrWhiteSpace(searchContent))
                return;
            await SearchAsync(searchContent, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
          
        }
    }

    private async Task SearchAsync(string searchContent, CancellationToken cancellationToken)
    {
        
        WeakReferenceMessenger.Default.Send(new FastSearchFlyoutMessage(true,searchContent));
    }

    /// <summary>
    /// 移动删除的显示动画
    /// </summary>
    [ObservableProperty] private bool _isShowBorder = false;

    public UserInfoService UserInfoService { get; set; }
    private readonly WebApiService _webApiService;
    public SearchViewModel SearchVM { get; set; }
    [ObservableProperty] private ThemeSwitchViewModel _themeSwitchVM;

    public TopBarViewModel()
    {
    }

    public TopBarViewModel(WebApiService webApiService, UserInfoService userInfoService,
        ThemeSwitchViewModel themeSwitchVM, SearchViewModel searchVM)
    {
        UserInfoService = userInfoService;
        _themeSwitchVM = themeSwitchVM;
        _webApiService = webApiService;
        SearchVM = searchVM;
        HeadImg = userInfoService.UserHead;
    }

    public void RefreshUserInfo()
    {
        HeadImg = UserInfoService.UserHead;
    }



    /// <summary>
    /// 新建文件夹
    /// </summary>
    [RelayCommand]
    private void OpenNewFolderDialog()
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("NewFolderDialog", true, new NewFolderMessage(null)));
    }

    /// <summary>
    /// 移动文件
    /// </summary>
    [RelayCommand]
    private void OpenMoveFilesDialog()
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("MoveFilesDialog", true, null));
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    [RelayCommand]
    private async Task RemoveFilesOrFolders()
    {
        WeakReferenceMessenger.Default.Send(new DefaultMsg(0, "TopBarDeleteActionMessage", null));
    }

    [RelayCommand]
    private async Task UpdateDialog()
    {
        WeakReferenceMessenger.Default.Send(new UploadFlyoutMessage(true));
    }

    /// <summary>
    /// 批量下载
    /// </summary>
    [RelayCommand]
    private void BatchDownload()
    {
        IsShowBorder = false;
        WeakReferenceMessenger.Default.Send(new FileTmMessage(2));
    }
}
