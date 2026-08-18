using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using drive_desktop.Models;

namespace drive_desktop.Services;

/// <summary>
/// 全局用户信息服务
/// </summary>
public partial class UserInfoService : ObservableObject
{
    private readonly WebApiService _webApiService;

    /// <summary>
    /// 当前登录用户信息实体
    /// </summary>
    [ObservableProperty]
    private ShowUserInfo _showUserInfo = new();
    /// <summary>
    /// 网盘信息
    /// </summary>
    [ObservableProperty]
    private CloudInfo _cloudInfo = new();
    /// <summary>
    /// 用户头像 Bitmap 对象
    /// </summary>
    [ObservableProperty]
    private Bitmap? _userHead;
    
    /// <summary>
    /// 用户当前所在目录Id
    /// </summary>
    [ObservableProperty]
    private string _currentDirectoryId;
 
    /// <summary>
    /// 用户当前所在目录
    /// </summary>
    [ObservableProperty]
    private string _currentDirectoryPath;
    private readonly IThemeService  _themeService;

    public UserInfoService(WebApiService webApiService,IThemeService  themeService)
    {
        _webApiService = webApiService;
        _themeService =  themeService;
    }

    /// <summary>
    /// 从服务器端异步拉取最新的用户信息
    /// </summary>
    public async Task ReUserInfo()
    {
        try
        {
          
            var res = await _webApiService.UserApi.GetUserInfo();
            if (res.Status == 0 && res.Data != null)
            {
               
                ShowUserInfo = res.Data;
                ReTheme();
                await LoadUserHeadImgAsync(res.Data.AvatarUrl);
              
            }
           
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载用户信息异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据头像名称拉取文件流并转换为 Avalonia Bitmap
    /// </summary>
    /// <param name="imgName">后端存储的头像文件名</param>
    public async Task LoadUserHeadImgAsync(string? imgName)
    {
        if (string.IsNullOrWhiteSpace(imgName))
            return;

        try
        {
            using var stream = await _webApiService.UserApi.GetUserHeadImg(imgName);
            if (stream != null)
            {
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                UserHead = new Bitmap(memoryStream);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载用户头像异常: {ex.Message}");
        }
    }


    /// <summary>
    /// 刷新主题
    /// </summary>
    public void ReTheme()
    {
        _themeService.CurrentTheme = _showUserInfo.Preferences.DarkMode ?  ThemeVariant.Dark : ThemeVariant.Light;
    }
    /// <summary>
    /// 清除用户信息及状态
    /// </summary>
    public void ClearUserInfo()
    {
        ShowUserInfo = new ShowUserInfo();
        UserHead?.Dispose();
        UserHead = null;
    }
}