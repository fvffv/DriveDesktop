using System;
using System.Collections.ObjectModel;
using drive_desktop.ViewModels;

namespace drive_desktop.Models;

public class DefaultMsg<T>
{
    public int Status { get; set; }
    public string Msg { get; set; }
    public T Data { get; set; }

  
    public DefaultMsg(int status,string msg, T data)
    {
        Status = status;
        Msg = msg;
        Data = data;
    }
}

public class DefaultMsg : DefaultMsg<object>
{

    public DefaultMsg(int status,string msg, object data) : base(status,msg, data)
    {
    }
}

/// <summary>
/// 窗口打开消息，用于消息总线
/// </summary>
/// <param name="WindowName"></param>
public record OpenWindowMessage(string WindowName,ViewModelBase ViewModel);

/// <summary>
/// 弹窗消息
/// </summary>
public record DialogMessage(string Name,bool IsShow,Object parameter,bool isEdit = false);
/// <summary>
/// 侧边栏菜单点击消息
/// </summary>
public record SidebarItemMessage(int index,string title,object parameter=null);
/// <summary>
/// 视图弹窗消息
/// </summary>
/// <param name="IsEdit"></param>
/// <param name="CustomView"></param>
/// <param name="CustomViews"></param>
public record CustomViewMessage(bool IsEdit,CustomView CustomView = null,ObservableCollection<CustomView> CustomViews = null);  
public record NewFolderMessage(Object parameter); 
/// <summary>
/// 文本预览消息
/// </summary>
/// <param name="Mode"></param>
/// <param name="FolderOrFileId"></param>
/// <param name="Name"></param>
public record TextViewMessage(int Type,UserFilesInfoItem item); 

public record FilePageMessage(string Path);
/// <summary>
/// 文件传输消息
/// </summary>
/// <param name="Type">0下载 1上传 2批量下载</param>
/// <param name="Item"></param>
/// <param name="mode">下载模式  一个是自己的文件 一个是分享链接的文件</param>
public record FileTmMessage(int Type,Object Item = null,string folderId = "",string folderPath = "",int mode=0);

/// <summary>
/// 分享文件下载所需的数据。
/// </summary>
public record ShareDownloadRequest(
    FileShareInfoDto ShareInfo,
    string ShareKey,
    string? Password);
/// <summary>
/// 快捷上传面板处的进度条
/// </summary>
/// <param name="type">0 原子加一</param>
public record UploadPanleProgressBarMsg(int type);
/// <summary>
/// 用于显示上传浮窗消息
/// </summary>
/// <param name="show"></param>
public record UploadFlyoutMessage(bool show);
/// <summary>
/// 用于显示快速搜索浮窗消息
/// </summary>
/// <param name="show"></param>
public record FastSearchFlyoutMessage(bool show,string msg);

/// <summary>
/// 音乐播放
/// </summary>
/// <param name="show"></param>
public record MusicPlayMsg(bool show,UserFilesInfoItem msg);

