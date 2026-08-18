using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Refit;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class SettingPageViewModel:ViewModelBase
{
    public SettingPageViewModel()
    {
    }

    private readonly ITopLevelProvider _topLevelProvider;
    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly AppConfigService _appConfigService;
    private readonly TopBarViewModel _topBarViewModel;
    private readonly SemaphoreSlim _preferencesSaveLock = new(1, 1);
    private bool _isSynchronizingPreferences;
    private bool _isSynchronizingLocalConfig;

    [ObservableProperty]
    private string _downloadLocation = string.Empty;

    [ObservableProperty]
    private string _downloadTaskCount = "1";

    [ObservableProperty]
    private string _uploadTaskCount = "1";

    [ObservableProperty]
    private bool _isDirectLinkEnabled;

    [ObservableProperty]
    private bool _isWebDavEnabled;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    public SettingPageViewModel(
        AppConfigService appConfigService,
        UserInfoService userInfoService,
        ITopLevelProvider topLevelProvider,
        WebApiService webApiService,
        TopBarViewModel topBarViewModel)
    {
        _appConfigService = appConfigService;
        _userInfoService = userInfoService;
        _topLevelProvider = topLevelProvider;
        _webApiService = webApiService;
        _topBarViewModel = topBarViewModel;

        SynchronizeLocalConfig();
        SynchronizePreferences();
    }

    /// <summary>
    /// 提供给设置页面绑定的全局用户信息服务。
    /// </summary>
    public UserInfoService UserInfoService => _userInfoService;

    partial void OnDownloadTaskCountChanged(string value)
    {
        SaveTaskCount(value, isDownload: true);
    }

    partial void OnUploadTaskCountChanged(string value)
    {
        SaveTaskCount(value, isDownload: false);
    }

    [RelayCommand]
    private async Task SelectDownloadDirectoryAsync(Window window)
    {
        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "选择默认下载目录",
                AllowMultiple = false
            });

        if (folders.Count == 0)
        {
            return;
        }

        try
        {
            DownloadLocation = folders[0].Path.LocalPath;
            _appConfigService.Config.DownloadLocation = DownloadLocation;
            _appConfigService.Save();
        }
        catch (Exception ex)
        {
            ShowError($"下载目录保存失败：{ex.Message}");
        }
    }

    private void SynchronizeLocalConfig()
    {
        _isSynchronizingLocalConfig = true;
        DownloadLocation = _appConfigService.Config.DownloadLocation;
        DownloadTaskCount = GetValidTaskCount(_appConfigService.Config.DownloadSemaphore).ToString();
        UploadTaskCount = GetValidTaskCount(_appConfigService.Config.UploadSemaphore).ToString();
        _isSynchronizingLocalConfig = false;

        var downloadCount = int.Parse(DownloadTaskCount);
        var uploadCount = int.Parse(UploadTaskCount);
        if (_appConfigService.Config.DownloadSemaphore != downloadCount ||
            _appConfigService.Config.UploadSemaphore != uploadCount)
        {
            _appConfigService.Config.DownloadSemaphore = downloadCount;
            _appConfigService.Config.UploadSemaphore = uploadCount;
            _appConfigService.Save();
        }
    }

    private void SaveTaskCount(string text, bool isDownload)
    {
        if (_isSynchronizingLocalConfig || _appConfigService is null)
        {
            return;
        }

        var count = GetValidTaskCount(text);
        var normalizedText = count.ToString();

        if (isDownload && DownloadTaskCount != normalizedText)
        {
            _isSynchronizingLocalConfig = true;
            DownloadTaskCount = normalizedText;
            _isSynchronizingLocalConfig = false;
        }
        else if (!isDownload && UploadTaskCount != normalizedText)
        {
            _isSynchronizingLocalConfig = true;
            UploadTaskCount = normalizedText;
            _isSynchronizingLocalConfig = false;
        }

        if (isDownload)
        {
            if (_appConfigService.Config.DownloadSemaphore == count)
            {
                return;
            }

            _appConfigService.Config.DownloadSemaphore = count;
        }
        else
        {
            if (_appConfigService.Config.UploadSemaphore == count)
            {
                return;
            }

            _appConfigService.Config.UploadSemaphore = count;
        }

        _appConfigService.Save();
    }

    private static int GetValidTaskCount(string? text)
    {
        var digits = new string((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return 1;
        }

        if (!long.TryParse(digits, out var value))
        {
            return int.MaxValue;
        }

        return value switch
        {
            < 1 => 1,
            > int.MaxValue => int.MaxValue,
            _ => (int)value
        };
    }

    private static int GetValidTaskCount(int value) => Math.Max(1, value);
    
    /// <summary>
    /// 选择头像文件
    /// </summary>
    /// <param name="w"></param>
    [RelayCommand]
    private async Task SelectHeadImgAsync(Window w)
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
                    Title = "选择需要上传的头像文件",
                    AllowMultiple = false,

                    FileTypeFilter =
                    [

                        new FilePickerFileType("头像文件")
                        {
                            Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                        }
                    ]
                });

        if (files.Count == 0)
        {
            return;
        }

        var selectedFile = files[0];

        try
        {
            await using var stream = await selectedFile.OpenReadAsync();
            var response = await _webApiService.UserApi.UploadAvatarAsync(
                new StreamPart(
                    stream,
                    selectedFile.Name,
                    GetContentType(selectedFile.Name)));

            if (response.Status != 0)
            {
                ShowError($"头像上传失败：{response.Msg}");
                return;
            }

            await _userInfoService.ReUserInfo();
            if (_userInfoService.UserHead is { } headImg)
            {
                _topBarViewModel.HeadImg = headImg;
            }
            Home.GlobalToastManager?.Show(
                new Toast("头像上传成功"),
                type: NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowError($"头像上传失败：{ex.Message}");
        }
    }

    partial void OnIsDirectLinkEnabledChanged(bool value)
    {
        if (!_isSynchronizingPreferences)
        {
            _ = SaveAdvancedPreferencesAsync();
        }
    }

    partial void OnIsWebDavEnabledChanged(bool value)
    {
        if (!_isSynchronizingPreferences)
        {
            _ = SaveAdvancedPreferencesAsync();
        }
    }

    private async Task SaveAdvancedPreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync();
        try
        {
            var preferences = _userInfoService.ShowUserInfo.Preferences;
            if (preferences is null)
            {
                ShowError("用户偏好尚未加载");
                return;
            }

            preferences.IsDirectLinkEnabled = IsDirectLinkEnabled;
            preferences.IsWebDAVEnabled = IsWebDavEnabled;

            var response = await _webApiService.UserApi.UpdateUserPreferencesAsync(preferences);
            if (response.Status != 0)
            {
                ShowError($"高级功能设置保存失败：{response.Msg}");
                await _userInfoService.ReUserInfo();
                SynchronizePreferences();
                return;
            }

            Home.GlobalToastManager?.Show(
                new Toast("高级功能设置已保存"),
                type: NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowError($"高级功能设置保存失败：{ex.Message}");
        }
        finally
        {
            _preferencesSaveLock.Release();
        }
    }

    private void SynchronizePreferences()
    {
        var preferences = _userInfoService?.ShowUserInfo.Preferences;
        if (preferences is null)
        {
            return;
        }

        _isSynchronizingPreferences = true;
        IsDirectLinkEnabled = preferences.IsDirectLinkEnabled;
        IsWebDavEnabled = preferences.IsWebDAVEnabled;
        _isSynchronizingPreferences = false;
    }

    /// <summary>
    /// 更新当前用户密码。
    /// </summary>
    [RelayCommand]
    private async Task UpdatePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            ShowError("请输入当前登录密码");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword) ||
            NewPassword.Length < 8 ||
            !NewPassword.Any(char.IsLetter) ||
            !NewPassword.Any(char.IsDigit))
        {
            ShowError("新密码至少需要 8 位，并同时包含字母和数字");
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ShowError("两次输入的新密码不一致");
            return;
        }

        try
        {
            var response = await _webApiService.UserApi.UpdatePasswordAsync(
                new UserPasswordEdit
                {
                    OldPassword = CurrentPassword,
                    NewPassword = NewPassword
                });

            if (response.Status != 0)
            {
                ShowError($"密码更新失败：{response.Msg}");
                return;
            }

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;

            Home.GlobalToastManager?.Show(
                new Toast("密码更新成功"),
                type: NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowError($"密码更新失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 保存昵称。
    /// </summary>
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        var nickname = _userInfoService.ShowUserInfo.Nickname?.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            ShowError("昵称不能为空");
            return;
        }

        try
        {
            var response = await _webApiService.UserApi.UpdateUserInfoAsync(
                new UserInfoEdit
                {
                    UserNick = nickname
                });

            if (response.Status != 0)
            {
                ShowError($"昵称保存失败：{response.Msg}");
                return;
            }

            await _userInfoService.ReUserInfo();
            Home.GlobalToastManager?.Show(
                new Toast("昵称保存成功"),
                type: NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowError($"昵称保存失败：{ex.Message}");
        }
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static void ShowError(string message)
    {
        
        Home.GlobalToastManager?.Show(
            new Toast(message),
            type: NotificationType.Error);
    }
}
