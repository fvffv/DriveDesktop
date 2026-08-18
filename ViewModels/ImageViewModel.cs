using System;
using System.IO;
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

public partial class ImageViewModel: ViewModelBase
{
    [ObservableProperty] private Bitmap _source;
    [ObservableProperty] private bool _isShowImageViewDialog = false;
    /// <summary>
    /// 控制加载界面
    /// </summary>
    [ObservableProperty] private bool _isLoaded = false;
    [ObservableProperty] private double _imgScale = 0.4;
    
    
    private readonly WebApiService _webApiService;
    // 绑定的旋转角度
    [ObservableProperty] 
    private double _rotationAngle = 0;
    [RelayCommand]
    private void Close()
    {
        Source = null;
        RotationAngle = 0;
        IsShowImageViewDialog = false;
    }

    public ImageViewModel()
    {
        
    }
    public ImageViewModel(WebApiService  webApiService)
    {
        _webApiService =  webApiService;

        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "ImageViewDialog")
                {
                    IsShowImageViewDialog = message.IsShow;
                }
                if (message is {Name:"ImageViewDialog",IsShow:true})
                {
                   LoadImage(message.parameter as UserFilesInfoItem);
                }
            });
    }

    /// <summary>
    /// 加载图片
    /// </summary>
    /// <param name="userFilesInfo"></param>
    public async Task LoadImage(UserFilesInfoItem userFilesInfo)
    {
        IsLoaded = false;
        DefaultMsg tmpkey = await _webApiService.FileApi.GetFileDownLoadTempKeyAsync(userFilesInfo.Id);
        if (tmpkey.Status!=0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取临时下载密钥失败:{tmpkey.Msg}"), 
                type: NotificationType.Error);
            return;
        }

        using var img = await _webApiService.FileApi.DownLoadKey(tmpkey.Data.ToString());
        using var memoryStream = new MemoryStream();
        await img.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        Source = new Bitmap(memoryStream);
        ImgScale = 0.4;
        IsLoaded = true;
    }


    /// <summary>
    /// 放大缩小按钮
    /// </summary>
    /// <param name="type"></param>
    [RelayCommand]
    private void ScaleButton(string parameter)
    {
        int.TryParse(parameter, out int type);
        if (type == 0)
        {
            if (ImgScale - 0.2 <0.1)
            {
                ImgScale = 0.1;
            }
            ImgScale -= 0.2;
        }
        else
        {
            ImgScale += 0.2;
        }
    }
    /// <summary>
    /// 旋转
    /// </summary>
    /// <param name="type"></param>
    [RelayCommand]
    private void RotateButton(string parameter)
    {
        int.TryParse(parameter, out int type);
        if (type == 0)
        {
            RotationAngle -= 90;
        }
        else
        {
            RotationAngle += 90;
        }
    }

   
}