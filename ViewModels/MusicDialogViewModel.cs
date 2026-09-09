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

public partial class MusicDialogViewModel:ViewModelBase
{
    private readonly WebApiService _webApiService;
    private readonly AppConfigService _appConfigService;
    [ObservableProperty]
    private bool _isOpen = false;
    [ObservableProperty]
    private string _url ;
    [ObservableProperty]
    private string _name ;
    [ObservableProperty]
    private bool _isPlay = true;
    public MusicDialogViewModel()
    {
        
    }
    public MusicDialogViewModel(WebApiService  webApiService,AppConfigService appConfigService)
    {
        _webApiService =  webApiService;
        _appConfigService = appConfigService;
        WeakReferenceMessenger.Default.Register<MusicPlayMsg>(this,
           void (recipient, message) =>
           {

               init(message);
           });
    }

    [RelayCommand]
    private async Task Close()
    {
        IsPlay = false;
        IsPlay = false;
        IsPlay = false;
        Url = null;
    }

    private async Task init(MusicPlayMsg message)
    {
        Name = message.msg.FileName;
        IsOpen = message.show;
        IsPlay = true;
        var info = await _webApiService.FileApi.GetFileDownLoadTempKeyAsync(message.msg.Id);
        if (info.Status == 0)
        {
            Url = $"{_appConfigService.Config.ServerIp.TrimEnd('/')}/api/Files/DownLoadKey/{info.Data}";
        }
        else
        {
            IsOpen = false;
            Home.GlobalToastManager?.Show(
                new Toast($"播放失败：{info.Msg}"),
                type: NotificationType.Error);
        }

    }
}