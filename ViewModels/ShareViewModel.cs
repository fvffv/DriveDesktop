using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;

namespace drive_desktop.ViewModels;

public partial class ShareViewModel : ViewModelBase
{
    private const double FullWindowWidth = 960;
    private const double CompactWindowWidth = 560;
    private const double IntroductionWidth = 432;
    private readonly UserInfoService _userInfoService;
    private readonly AppConfigService _appConfigService;

    public ShareViewModel(FileShareInfoDto shareInfo, string shareKey, UserInfoService userInfoService,
        AppConfigService appConfigService)
    {
        _userInfoService = userInfoService;
        _appConfigService = appConfigService;
        ShareInfo = shareInfo ?? throw new ArgumentNullException(nameof(shareInfo));
        ShareKey = shareKey ?? throw new ArgumentNullException(nameof(shareKey));
        IconInfo = ConstantResourceService.FileIconHelper.GetFileInfo(shareInfo.Name);
    }

    public FileShareInfoDto ShareInfo { get; }

    public string ShareKey { get; }

    public FileTypeInfo IconInfo { get; }

    public string Nickname => string.IsNullOrWhiteSpace(ShareInfo.Nickname)
        ? "用户"
        : ShareInfo.Nickname;

    public string AvatarUrl => _appConfigService.Config.ServerIp + "/driveassets/avatar/" + ShareInfo.AvatarUrl;

    public string PublisherText => $"{Nickname} 向你分享了文件";

    public string PublishedAtText => $"{ShareInfo.CreationTime:yyyy-MM-dd HH:mm} 发布";

    public string Name => ShareInfo.Name;

    public string FileSizeText => FormatBytes(ShareInfo.SizeInBytes);

    public string ExpirationText => FormatExpiration(ShareInfo.EndValidity);

    public bool HasPassword => ShareInfo.IsPassword;

    public bool HasIntroduction => !string.IsNullOrWhiteSpace(ShareInfo.Introduction);

    public string Introduction => ShareInfo.Introduction?.Trim() ?? string.Empty;

    public string IntroductionAuthorText => $"{Nickname} 的寄语";

    public double WindowWidth => HasIntroduction ? FullWindowWidth : CompactWindowWidth;

    public double IntroductionPanelWidth => HasIntroduction ? IntroductionWidth : 0;

    [ObservableProperty] private bool _isPasswordDialogOpen;

    [ObservableProperty] private string _downloadPassword = string.Empty;

    [ObservableProperty] private string _passwordValidationMessage = string.Empty;

    partial void OnDownloadPasswordChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            PasswordValidationMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (HasPassword)
        {
            DownloadPassword = string.Empty;
            PasswordValidationMessage = string.Empty;
            IsPasswordDialogOpen = true;
            return;
        }

        await DownloadFileAsync(null);
    }

    [RelayCommand]
    private void CancelPasswordDownload()
    {
        IsPasswordDialogOpen = false;
        DownloadPassword = string.Empty;
        PasswordValidationMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmPasswordDownload()
    {
        if (string.IsNullOrWhiteSpace(DownloadPassword))
        {
            PasswordValidationMessage = "请输入提取密码";
            return;
        }

        var password = DownloadPassword;
        IsPasswordDialogOpen = false;
        await DownloadFileAsync(password);
    }

    /// <summary>
    /// 下载入口。无密码分享传入 null，有密码分享传入用户输入的密码。
    /// </summary>
    private Task DownloadFileAsync(string? password)
    {
        WeakReferenceMessenger.Default.Send(new FileTmMessage( 0, new ShareDownloadRequest(ShareInfo, ShareKey, password), mode: 1));
        return Task.CompletedTask;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatExpiration(DateTime endValidity)
    {
        var remaining = endValidity - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "已过期";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{Math.Ceiling(remaining.TotalDays):0} 天后过期";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{Math.Ceiling(remaining.TotalHours):0} 小时后过期";
        }

        return $"{Math.Max(1, Math.Ceiling(remaining.TotalMinutes)):0} 分钟后过期";
    }
}