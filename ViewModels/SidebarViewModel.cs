using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
    /// <summary>
    /// 侧边视图菜单
    /// </summary>
    [ObservableProperty] public ObservableCollection<CustomView> customViews = new ObservableCollection<CustomView>();
    /// <summary>
    /// 已使用容量
    /// </summary>
    [ObservableProperty]
    private string _usedSpaceInBytes = "0GB";
    /// <summary>
    /// 总容量
    /// </summary>
    [ObservableProperty]
    private string _totalSpaceInBytes = "0GB";
    [ObservableProperty]
    private bool _isLight = true;
    /// <summary>
    /// 进度条百分比
    /// </summary>
    [ObservableProperty]
    private double _percentage = 0;

    /// <summary>
    /// 导航是否展开状态
    /// </summary>
    [ObservableProperty] private bool isExpand = false;
    [ObservableProperty]
    private string _driveName = "居家网盘";
    private readonly WebApiService _webApiService;
    private readonly ConstantResourceService _constantResourceService;
    private readonly UserInfoService _userInfoService;
    private readonly IThemeService _themeService;
    
    
    [RelayCommand]
    private void EditCustomView(CustomView cv)
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("CustomViewEdit", true, new CustomViewMessage(true, cv, CustomViews)));
    }

    public SidebarViewModel()
    {
    }

    /// <summary>
    /// 初始化视图列表
    /// </summary>
    private void InitCustomViews()
    {
        var customViews = _userInfoService.ShowUserInfo.Preferences?.CustomView;
        CustomViews = new ObservableCollection<CustomView>(
            (customViews ?? []).Select(x => new CustomView()
            {
                Name = x.Name,
                Icon = _constantResourceService.GetSidebarIcon(x.Icon),
                Color = ConstantResourceService.FileIconHelper.GetSideIconColor(x.Icon),
                Keywords = x.Keywords,
                Type = x.Type,
            }));
        
      
    }
    /// <summary>
    /// 加载储存容量信息
    /// </summary>
    private async Task InitStorageCapacityInfo()
    {
       var info =  await _webApiService.FileApi.GetUserStorageCapacityInfoAsync();
       if (info.Status==0)
       {
           UsedSpaceInBytes = Math.Round(info.Data.UsedSpaceInBytes / 1024 /1024 /1024, 2) +" GB" ;
           TotalSpaceInBytes = Math.Round(info.Data.TotalSpaceInBytes / 1024 /1024 /1024, 2)+" GB" ;
           Percentage = (info.Data.UsedSpaceInBytes / info.Data.TotalSpaceInBytes) * 100;
       }
    }
    public SidebarViewModel(IThemeService themeService,UserInfoService userInfoService, AppConfigService appConfigService,
        WebApiService webApiService, ConstantResourceService constantResourceService)
    {
        _themeService = themeService;
        _webApiService = webApiService;
        _constantResourceService = constantResourceService;
        _userInfoService = userInfoService;
        IsLight = _themeService.CurrentTheme == ThemeVariant.Light;
    }

    public async Task InitializeAsync()
    {
        DriveName = string.IsNullOrWhiteSpace(_userInfoService.CloudInfo.Name)
            ? "居家网盘"
            : _userInfoService.CloudInfo.Name;
        InitCustomViews();
        await InitStorageCapacityInfo();
    }

    [RelayCommand]
    private void SwitchView(object view)
    {
        //字符串则是默认的固定view   //是CustomView对象则是视图菜单
        if (view is string)
        {
            int i = int.Parse((string)view);
            switch (i)
            {
                case 0:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "我的文件"));
                    break;
                case 1:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "搜索"));
                    break;
                case 2:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "传输管理"));
                    break;
                case 3:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "分享管理"));
                    break;
                case 4:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "统计看板"));
                    break;
                case 5:
                    WeakReferenceMessenger.Default.Send(new SidebarItemMessage(i, "设置"));
                    break;
                default:
                    break;
            }

            Debug.WriteLine($"{i}");
        }

        if (view is CustomView v)
        {
            if (v.Type==0)
            {
                WeakReferenceMessenger.Default.Send(new SidebarItemMessage(1,"搜索", v));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new SidebarItemMessage(0,"我的文件", v));
            }
           
      
        }
    }

    [RelayCommand]
    private async Task AutoCustomView()
    {
        int i = _constantResourceService.AddDefaultCustomView(CustomViews);

        if (i == 0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"已经全部添加了~"),
                type: NotificationType.Success
            );
        }

        var info = await _webApiService.UserApi.UpdateUserViewAsync(CustomViews.Select(x => new CustomViewDto
        {
            Name = x.Name, Keywords = x.Keywords, Icon = _constantResourceService.GetSidebarClassName(x.Icon),
            Type = x.Type
        }).ToArray());
        if (info?.Status == 0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"已添加{i}项~"),
                type: NotificationType.Success
            );
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast(info.Msg),
                type: NotificationType.Warning
            );
        }
    }

    [RelayCommand]
    private async Task AddCustomView()
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("CustomViewEdit", true, new CustomViewMessage(false,CustomViews: CustomViews)));
    }

    [RelayCommand]
    private async Task DelCustomView(CustomView cv)
    {
        CustomViews.Remove(cv);
        var info = await _webApiService.UserApi.UpdateUserViewAsync(CustomViews.Select(x => new CustomViewDto
        {
            Name = x.Name, Keywords = x.Keywords, Icon = _constantResourceService.GetSidebarClassName(x.Icon),
            Type = x.Type
        }).ToArray());
        if (info.Status == 0)
        {
            Home.GlobalToastManager?.Show(
                new Toast("删除视图成功"),
                type: NotificationType.Success
            );
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"错误:{info.Msg}"),
                type: NotificationType.Warning
            );
        }

       
    }

    /// <summary>
    /// 主题切换
    /// </summary>
    /// <param name="theme"></param>
    [RelayCommand]
    private async Task ThemeSwitch(string theme)
    {
        if (theme == "0") _themeService.CurrentTheme = ThemeVariant.Light;
        if (theme == "1") _themeService.CurrentTheme = ThemeVariant.Dark;
        
        var res = await _webApiService.UserApi.GetUserInfo();
        if (res.Status == 0 && res.Data != null)
        {
           _userInfoService.ShowUserInfo  = res.Data;
           _userInfoService.ShowUserInfo.Preferences.DarkMode = _themeService.CurrentTheme == ThemeVariant.Dark;
           _ = _webApiService.UserApi.UpdateUserPreferencesAsync(_userInfoService.ShowUserInfo.Preferences);
        }
        
        
       
    }


   

    
}
