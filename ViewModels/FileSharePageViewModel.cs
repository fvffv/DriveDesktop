using System;
using System.Collections.ObjectModel;
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

public partial class FileSharePageViewModel : ViewModelBase
{
    /// <summary>
    /// 展示的数据
    /// </summary>
    [ObservableProperty] private ObservableCollection<ShareInfoPrivateGroup> _shareInfos = new();

    /// <summary>
    /// api直接返回的数据
    /// </summary>
    private ShareInfoPrivateDto[] RawData;

    /// <summary>
    /// 处理后的原数据
    /// </summary>
    private ObservableCollection<ShareInfoPrivateGroup> FormatData = new();

    /// <summary>
    /// 搜索文本
    /// </summary>
    [ObservableProperty] private string _searchText;

    
    /// <summary>
    /// 失效数量
    /// </summary>
    [ObservableProperty] private int _failNum;
    /// <summary>
    /// 有效数量
    /// </summary>
    [ObservableProperty] private int _effNum;
    /// <summary>
    /// 有效占比
    /// </summary>
    [ObservableProperty] private int _effectiveProportion;
    private bool _scheduledTask = false;
    /// <summary>
    /// 搜索内容改变
    /// </summary>
    /// <param name="text"></param>
    partial void OnSearchTextChanged(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            ShareInfos = FormatData;
        }
        else
        {
           
            var filteredRawData = RawData.Where(x =>
                // 优先查文件名
                (x.FileName != null && x.FileName.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
            
                // 查提取码
                (x.Password != null && x.Password.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
            
                // 查链接
                x.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                x.ShareFileId.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            
                // 查简介
                (x.Introduction != null && x.Introduction.Contains(text, StringComparison.OrdinalIgnoreCase))
            );
            ShareInfos =  new ObservableCollection<ShareInfoPrivateGroup>(filteredRawData
                .GroupBy(x => x.ShareFileId)
                .Select(group => new ShareInfoPrivateGroup
                {
                    ShareFileId = group.Key.ToString(),
                    FileName = group.First().FileName,
                    IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(group.First().FileName),
                    ShareInfoPrivateItems = new ObservableCollection<ShareInfoPrivateItem>(
                        group.Select(ShareInfoPrivateItem.ToShareInfoPrivateItem)
                    )
                }));
        }
    }
    
    public FileSharePageViewModel()
    {
    }
    

    private readonly ITopLevelProvider _topLevelProvider;
    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly AppConfigService _appConfigService;
    public FileSharePageViewModel(AppConfigService appConfigService,UserInfoService userInfoService,ITopLevelProvider topLevelProvider, WebApiService webApiService)
    {
        _appConfigService =  appConfigService;
        _userInfoService = userInfoService;
        _topLevelProvider = topLevelProvider;
        _webApiService = webApiService;
        LoadShareInfo();
        
        WeakReferenceMessenger.Default.Register<SidebarItemMessage>(this,
            (recipient, message) =>
            {
                if (message.index==3)
                {
                    if (_scheduledTask)
                    {
                        return;
                    }
                    _scheduledTask = true;
                    RefreshStatisticalData();
                }
                else
                {
                    _scheduledTask = false;
                }
            });
    }

    private async Task LoadShareInfo()
    {
        var info = await _webApiService.FileApi.GetShareFilesInfoPrivateAsync();
        if (info.Status == 0)
        {
            RawData = info.Data;
            //先按文件id分组 再分别把每一组映射到ShareInfoPrivateGroup
            FormatData = new ObservableCollection<ShareInfoPrivateGroup>(info.Data
                .GroupBy(x => x.ShareFileId)
                .Select(group => new ShareInfoPrivateGroup
                {
                    ShareFileId = group.Key.ToString(),
                    FileName = group.First().FileName,
                    IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(group.First().FileName),
                    ShareInfoPrivateItems = new ObservableCollection<ShareInfoPrivateItem>(
                        group.Select(ShareInfoPrivateItem.ToShareInfoPrivateItem)
                    )
                }));
            ShareInfos = FormatData;
        }
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task OpenShareFile(ShareInfoPrivateGroup item)
    {
        var item2 = new UserFilesInfoItem()
        {
            Id = item.ShareFileId,
            FileName = item.FileName,
        };
        WeakReferenceMessenger.Default.Send(new DialogMessage("ShareFileDialog", true, item2));
    }


    /// <summary>
    /// 复制分享id
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task CopyShareId(ShareInfoPrivateItem item)
    {
        await _topLevelProvider.GetTopLevel()?.Clipboard
            .SetTextAsync("share_"+item.ShareId);
        Home.GlobalToastManager?.Show(
            new Toast("复制成功"),
            type: NotificationType.Success
        );
    }

    /// <summary>
    /// 编辑链接
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task EditShare(ShareInfoPrivateItem item)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("ShareFileDialog", true, item, true));
    }

    /// <summary>
    /// 删除链接
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task DelShare(ShareInfoPrivateItem item)
    {
        var info = await _webApiService.FileApi.UpdateShareFileInfoAsync(item.ShareId, true);
        if (info.Status == 0)
        {
            var group = FormatData.FirstOrDefault(x => x.ShareFileId == item.ShareFileId);
            group?.ShareInfoPrivateItems.Remove(item);
            if (group?.ShareInfoPrivateItems.Count == 0)
            {
                FormatData.Remove(group);
            }
            Home.GlobalToastManager?.Show(
                new Toast("删除成功"),
                type: NotificationType.Success
            );
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"删除失败:{info.Msg}"),
                type: NotificationType.Success
            );
        }
    }

    /// <summary>
    /// 刷新
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    private async Task Refresh()
    {
       await LoadShareInfo();
    }
    
    
    /// <summary>
    /// 刷新统计数据
    /// </summary>
    /// <param name="item"></param>
    private async Task RefreshStatisticalData()
    {
        while (true)
        {
            if (_scheduledTask == false)
            {
                return;
            }

           FailNum =  RawData.Count(x => x.EndValidity < DateTime.Now);
           EffNum =  RawData.Count(x => x.EndValidity >= DateTime.Now);
            EffectiveProportion = (int)(Math.Round((double)EffNum / (double)RawData.Length, 2) * 100);
            await Task.Delay(3000);
        }
    }


    /// <summary>
    /// 打开文件分享窗口
    /// </summary>
    [RelayCommand]
    private async Task OpenShareId(ShareInfoPrivateItem item)
    {
        var info = await _webApiService.FileApi.GetShareInfoAsync(item.ShareId);
        if (info.Status == 0 && info.Data is not null)
        {
            var shareview = new ShareView
            {
                DataContext = new ShareViewModel(info.Data, item.ShareId,_userInfoService,_appConfigService)
            };
            shareview.Show();
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"打开分享失败：{info.Msg}"),
                type: NotificationType.Error
            );
        }
       
    }
}
