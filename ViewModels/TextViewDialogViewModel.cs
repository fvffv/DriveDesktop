using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using LiveMarkdown.Avalonia;
using Ursa.Controls;

namespace drive_desktop.ViewModels;

public partial class TextViewDialogViewModel :ViewModelBase
{
    /// <summary>
    /// 标题
    /// </summary>
    [ObservableProperty]
    private string title = "";
    
    /// <summary>
    /// 显示模式 true显示代码框 false显示md预览
    /// </summary>
    [ObservableProperty]
    private bool _showMode = true;
    /// <summary>
    /// md文本对象
    /// </summary>
    [ObservableProperty]
    private ObservableStringBuilder _mdText = new ObservableStringBuilder() ;
    
    // TextMate绑定的文本
    [ObservableProperty] 
    private TextDocument _fileContentDocument =new TextDocument();
    
    /// <summary>
    /// 是否支持预览
    /// </summary>
    [ObservableProperty]
    private bool _isView = true;
    [RelayCommand]
    private async Task Close()
    {
        IsShowTextViewDialog = false;
        await Task.Delay(500);
        MdText = new ObservableStringBuilder();
        MdText = null;
        FileContentDocument.Text = string.Empty;
        GC.Collect();
       
    }
    [ObservableProperty] private bool _isShowTextViewDialog = false;
    private readonly WebApiService _webApiService;
    public TextViewDialogViewModel()
    {
    }

    public TextViewDialogViewModel(WebApiService webApiService)
    {
        _webApiService = webApiService;
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "TextViewDialog")
                {
                    IsShowTextViewDialog = message.IsShow;
                }
                if (message is { Name: "TextViewDialog", IsShow: true })
                {
                    
                
                    //设置模式
                    ShowMode = (message.parameter as TextViewMessage).Type == 0 ? true : false;
                    //设置切换预览可视状态
                    IsView = !ShowMode;
                    LoadTextAsync(message.parameter as TextViewMessage);
                    
                }
            });
    }

/// <summary>
/// 加载文本
/// </summary>
/// <param name="userFilesInfo"></param>
    private async Task LoadTextAsync(TextViewMessage msg)
    {
        Title = msg.item.FileName;
        DefaultMsg tmpkey = await _webApiService.FileApi.GetFileDownLoadTempKeyAsync(msg.item.Id);
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
        byte[] fileBytes = memoryStream.ToArray();
        string contentText =ConstantResourceService.DetectEncodingAndDecodeText(fileBytes);

        //如果是md文件就渲染md格式
        if (msg.Type == 1)
        {
            MdText = new ObservableStringBuilder(contentText);
        }
        //否则直接加载文本
        FileContentDocument = new TextDocument(contentText) ;
    }
    
}