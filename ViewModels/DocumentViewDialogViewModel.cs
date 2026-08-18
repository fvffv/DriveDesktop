using System;
using System.IO;
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

public partial class DocumentViewDialogViewModel :ViewModelBase
{
    [ObservableProperty] private bool _isShowDialog = false;
    private readonly WebApiService _webApiService;
    [ObservableProperty]
    private byte[]? _docxDocument;
    [ObservableProperty]
    private byte[]? _pptxDocument;
    [ObservableProperty]
    private byte[]? _xlsxDocument;
    [ObservableProperty]
    private byte[]? _pdfDocument;
    /// <summary>
    /// 控制加载界面
    /// </summary>
    [ObservableProperty] private bool _isLoaded = false;
    /// <summary>
    /// 展示的组件
    /// </summary>
    [ObservableProperty] private int _pageIndex = 0;

    public DocumentViewDialogViewModel()
    {
        
    }
    public DocumentViewDialogViewModel(WebApiService webApiService)
    {
        _webApiService = webApiService;
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "DocumentViewDialog")
                {
                    IsLoaded = true;
                    IsShowDialog = message.IsShow;
                    LoadDocument(message.parameter as UserFilesInfoItem);
                }
               
            });
    }
    [RelayCommand]
    private async Task Close()
    {
        
        IsShowDialog = false;
        await Task.Delay(500);
        DocxDocument  = null;
        PptxDocument = null;
        XlsxDocument = null;
        PdfDocument = null;
        GC.Collect();
    }

    private async Task LoadDocument(  UserFilesInfoItem item)
    {
     
        DefaultMsg tmpkey = await _webApiService.FileApi.GetFileDownLoadTempKeyAsync(item.Id);
        if (tmpkey.Status!=0)
        {
            Home.GlobalToastManager?.Show(
                new Toast($"获取临时下载密钥失败:{tmpkey.Msg}"), 
                type: NotificationType.Error);
            return;
        }
        //下载文本
        using var text = await _webApiService.FileApi.DownLoadKey(tmpkey.Data.ToString());
        using var memoryStream = new MemoryStream();
        await text.CopyToAsync(memoryStream);
        if (Path.GetExtension(item.FileName) == ".docx")
        {
            PageIndex = 0;
            DocxDocument = memoryStream.ToArray();
        }if (Path.GetExtension(item.FileName) == ".pptx")
        {
            PageIndex = 1;
            PptxDocument = memoryStream.ToArray();
        }
        if (Path.GetExtension(item.FileName) == ".xlsx")
        {
            PageIndex = 2;
            XlsxDocument = memoryStream.ToArray();
        }
        if (Path.GetExtension(item.FileName) == ".pdf")
        {
            PageIndex = 3;
            PdfDocument = memoryStream.ToArray();
        }
        IsLoaded = false;
    }
}