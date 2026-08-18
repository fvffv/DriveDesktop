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

public partial class NewFolderDialogViewModel :ViewModelBase
{
    /// <summary>
    /// 文件夹名称
    /// </summary>
    [ObservableProperty]
    private string _folderOrFileName;
    /// <summary>
    /// 标题
    /// </summary>
    [ObservableProperty]
    private string title = "新建文件夹";

    /// <summary>
    /// 0是新建文件夹 1是编辑文件名 2是文件夹名
    /// </summary>
    private int Mode = 0;
    /// <summary>
    /// 文件或文件夹id
    /// </summary>
    private string FolderOrFileId = string.Empty;
    [ObservableProperty]
    private ButtonStatus _buttonStatus = new ButtonStatus("确定");
    [ObservableProperty] private bool _isShowNewFolderDialog = false;
    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly FilePageViewModel _filePageViewModel;
    private  object FileOrFolderItem;
    [RelayCommand]
    private void Close()
    {
        IsShowNewFolderDialog = false;
    }

    public NewFolderDialogViewModel()
    {
        
    }
    public NewFolderDialogViewModel(WebApiService  webApiService, UserInfoService userInfoService,FilePageViewModel filePageViewModel)
    {
        _webApiService = webApiService;
        _userInfoService = userInfoService;
        _filePageViewModel =  filePageViewModel;
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "NewFolderDialog")
                {
                    IsShowNewFolderDialog = message.IsShow;
                }

                if (message is { Name: "NewFolderDialog",IsShow: true} )
                {
                    //判断是哪种模式做出对应UI改变
                    NewFolderMessage par = message.parameter as NewFolderMessage;
                    FileOrFolderItem =  par.parameter;
                    if (par.parameter  == null)
                    {
                        Mode = 0;
                        Title = "新建文件夹";
                    }

                    if (par.parameter is UserDirsInfoItem di)
                    {
                        Mode = 2;
                        FolderOrFileId = di.Id;
                        FolderOrFileName = di.FolderName;
                        Title = "重命名文件夹";
                    }

                    if (par.parameter is UserFilesInfoItem fi)
                    {
                        Mode = 1;
                        FolderOrFileId = fi.Id;
                        FolderOrFileName = fi.FileName;
                        Title = "重命名文件";
                    }

                  
                    
                }
            });
    }
    /// <summary>
    /// 更新文件夹名称
    /// </summary>
    [RelayCommand]
    private async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(FolderOrFileName)) return;
        DefaultMsg info=null;
        ButtonStatus.Begin("提交中");
        switch (Mode)
        {
            case 0:
                 info =await _webApiService.FileApi.CreateFolderAsync(_userInfoService.CurrentDirectoryId, FolderOrFileName);
                break;
            case 1:
            case 2:
                info =await _webApiService.FileApi.RenameFileOrDirAsync(new FileOrDirReNameInfo(){Id = FolderOrFileId,NewName = FolderOrFileName,Type = Mode-1});
                break;
        }


        if (info.Status==0)
        {
            if (FileOrFolderItem is UserDirsInfoItem di)
            {
                di.FolderName = FolderOrFileName;
            }

            if (FileOrFolderItem is UserFilesInfoItem fi)
            {
                fi.FileName = FolderOrFileName;
            }

            if (Mode == 0)
            {
                _filePageViewModel.RefreshCurrentDirectoryCommand.Execute(null);
            }
     
            Close();
        }
        else
        {
                Home.GlobalToastManager?.Show(
                new Toast($"错误:{info.Msg}"),
                type: NotificationType.Error
            );
        }
        ButtonStatus.End();
    }
    
    
}