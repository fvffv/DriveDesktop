using System;
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

public partial class ShareFileDialogViewModel : ViewModelBase
{
    /// <summary>
    /// 标题
    /// </summary>
    [ObservableProperty] private string _title = "新建分享链接";

    /// <summary>
    /// 是否是编辑模式
    /// </summary>
    [ObservableProperty] private bool _isEdit = false;
    private ShareInfoPrivateItem ShareInfo;
    
    [ObservableProperty] private bool _isShowShareFileDialog = false;

    /// <summary>
    /// 页码
    /// </summary>
    [ObservableProperty] private int _selectedIndex = 0;

    /// <summary>
    /// 文件名
    /// </summary>
    [ObservableProperty] private string _fileName = string.Empty;

    /// <summary>
    /// 分享链接
    /// </summary>
    [ObservableProperty] private string _shareUrl = "";
    /// <summary>
    /// 是否需要密码
    /// </summary>
    [ObservableProperty] private bool _isNeedPs;
    /// <summary>
    /// 表单对象
    /// </summary>
    [ObservableProperty] private FileShareData _fileShareData = new FileShareData();
    /// <summary>
    /// 创建按钮状态对象
    /// </summary>
    [ObservableProperty] private ButtonStatus _createButtonStatus ;
    private readonly WebApiService _webApiService;
    private readonly AppConfigService _appConfigService;
    private readonly ITopLevelProvider _topLevelProvider;
    private UserFilesInfoItem item;

    public ShareFileDialogViewModel()
    {
    }

    public ShareFileDialogViewModel(ITopLevelProvider topLevelProvider,AppConfigService appConfigService, WebApiService webApiService)
    {
        _appConfigService = appConfigService;
        _webApiService = webApiService;
        _topLevelProvider = topLevelProvider;
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "ShareFileDialog")
                {
                    IsShowShareFileDialog = message.IsShow;
                }

                if (message is { Name: "ShareFileDialog", IsShow: true,isEdit:false } )
                {
                    Title = "新建分享链接";
                    IsEdit = false;
                    FileShareData.BeginValidity = DateTime.Now;
                    FileShareData.EndValidity = DateTime.Now.AddDays(3);
                    
                    item = message.parameter as UserFilesInfoItem;
                    FileName = item?.FileName ?? string.Empty;
                    CreateButtonStatus = new ButtonStatus("生成分享链接");
                }

                //编辑模式
                if (message is { Name: "ShareFileDialog", IsShow: true, isEdit: true })
                {
                    Title = "编辑分享链接";
                    ShareInfoPrivateItem  item = message.parameter as ShareInfoPrivateItem;
                    ShareInfo = item;
                    FileName = ShareInfo?.FileName ?? string.Empty;
                    FileShareData.ShareFileId = ShareInfo.ShareFileId;
                    FileShareData.BeginValidity = ShareInfo.BeginValidity;
                    FileShareData.EndValidity =  ShareInfo.EndValidity;
                    FileShareData.Introduction =  ShareInfo.Introduction;
                    if (ShareInfo.Password == "无")
                    {
                        FileShareData.Password = "";
                        IsNeedPs  = false;
                    }
                    else
                    {
                        FileShareData.Password =  ShareInfo.Password;
                        IsNeedPs = true;
                    }
           
                    IsEdit = true;
                    CreateButtonStatus = new ButtonStatus("更新分享链接");
                }
            });
    }

    [RelayCommand]
    private async Task Close()
    {
        
        IsShowShareFileDialog = false;
        await Task.Delay(500);
        SelectedIndex = 0;
        GC.Collect();
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    [RelayCommand]
    private async Task CreateFileShare()
    {
        //表单验证
        if (!_webApiService.FormValidation(FileShareData.GetValidationResult(), Home.GlobalToastManager)) return;
        if (IsEdit)
        {
            CreateButtonStatus.Begin("更新中");
            if (!IsNeedPs)
            {
                FileShareData.Password = null;
            }
            else
            {
                FileShareData.Password = ConstantResourceService.GenerateRandomString(4);
            }
            var info = await _webApiService.FileApi.UpdateShareFileInfoAsync(ShareInfo.ShareId,false,FileShareData);
            if (info.Status == 0)
            {
         
                ShareInfo.BeginValidity = FileShareData.BeginValidity ;
                ShareInfo.EndValidity =FileShareData.EndValidity  ; 
                ShareInfo.Introduction = FileShareData.Introduction ;
                if (IsNeedPs)
                {
                    ShareInfo.Password = FileShareData.Password;
                }
                else
                {
                    ShareInfo.Password = "无";
                }
                ShareInfo.PasswordIcon = ShareInfo.Password == null ? "\uf09c" : "\uf023";
                ShareInfo.Status = ShareInfo.EndValidity < DateTime.Now;
                ShareInfo.StatusIcon = ShareInfo.EndValidity < DateTime.Now ? "\uf06a" : "\uf058";
                
                Home.GlobalToastManager?.Show(
                    new Toast($"更新成功: {info.Msg}"),
                    type: NotificationType.Success
                );
                await Close();
            }
            else
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"更新失败: {info.Msg}"),
                    type: NotificationType.Error
                );
            }
            CreateButtonStatus.End();
        }
        else
        {
            CreateButtonStatus.Begin("创建中");
            FileShareData.ShareFileId = item.Id;
            var info = await _webApiService.FileApi.CreateShareKeyAsync(FileShareData);

            if (info.Status == 0)
            {
                //拼接生成url
                ShareUrl = $"{_appConfigService.Config.ServerIp}/share/{info.Data}";
                SelectedIndex = 1;
            }
            else
            {
                Home.GlobalToastManager?.Show(
                    new Toast($"创建失败: {info.Msg}"),
                    type: NotificationType.Error
                );
            }
            CreateButtonStatus.End();
        }
       
    }

    /// <summary>
    /// 复制分享链接
    /// </summary>
    [RelayCommand]
    private async Task CopyFileShareUrl()
    {
        if (string.IsNullOrEmpty(FileShareData.Password))
        {
            await _topLevelProvider.GetTopLevel().Clipboard.SetTextAsync($"链接: {ShareUrl}");
        }
        else
        {
           await _topLevelProvider.GetTopLevel().Clipboard.SetTextAsync($"链接: {ShareUrl} 提取码: {FileShareData.Password}");
        }
        Home.GlobalToastManager?.Show(
            new Toast($"复制成功"),
            type: NotificationType.Success
        );
    }
}