using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;

namespace drive_desktop.ViewModels;

public partial class FilePropertiesDialogViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isShowFilePropertiesDialog = false;
    private readonly WebApiService _webApiService;
    [ObservableProperty]
    private UserFilesInfoItem _itemInfo;
    [ObservableProperty]
    private string _path = "加载中";
    public FilePropertiesDialogViewModel()
    {
        
    }
    public FilePropertiesDialogViewModel(WebApiService  webApiService)
    {
        _webApiService =  webApiService;

        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "FilePropertiesDialog")
                {
                    IsShowFilePropertiesDialog = message.IsShow;
                }
                if (message is {Name:"FilePropertiesDialog",IsShow:true})
                {
                 
                    if (message.parameter is UserFilesInfoItem item)
                    {
                        ItemInfo = item;
                        GetFilePath(item);
                    }
                }
            });
    }
    [RelayCommand]
    private void Close()
    {
        WeakReferenceMessenger.Default.Send(new DialogMessage("FilePropertiesDialog", false, null));
    }

    /// <summary>
    /// 获取文件路径
    /// </summary>
    /// <param name="item"></param>
    private async Task GetFilePath(UserFilesInfoItem item)
    {
        var info = await _webApiService.FileApi.GetFullFolderPathAsync(item.FolderId);
        Path = info.Data.ToString();
    }
    
}