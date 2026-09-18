using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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

public partial class CustomViewEditViewModel : ViewModelBase
{
    /// <summary>
    /// 是否是编辑模式
    /// </summary>
    [ObservableProperty] private bool _isEdit = false;

    [ObservableProperty] private string _title = "包含关键字";
    [ObservableProperty] private string _inputTitle = "多关键字用逗号隔开 (例如: .pdf, 报告) 不超过20个";

    /// <summary>
    /// 临时CustomView对象  
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeSelect))]
    [NotifyPropertyChangedFor(nameof(KeywordString))]
    private CustomView _tempCustomView;

    /// <summary>
    /// 类型转换bool
    /// </summary>
    public bool TypeSelect => TempCustomView?.Type == 0;

    /// <summary>
    /// 数组转字符串
    /// </summary>
    public string KeywordString
    {
        //将数组转成逗号分隔的字符串
        get => TempCustomView?.Keywords != null ? string.Join(",", TempCustomView.Keywords) : "";

        //将用户输入的字符串重新切割成数组，存回对象里
        set
        {
            if (TempCustomView != null && !string.IsNullOrWhiteSpace(value))
            {
                // 用逗号分割，并且自动剔除空格和空项
                TempCustomView.Keywords = value.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .ToArray();

                // 通知 UI 绑定已更新
                OnPropertyChanged(nameof(KeywordString));
            }
        }
    }

    /// <summary>
    /// 保存按钮状态
    /// </summary>
    [ObservableProperty] public ButtonStatus saveButtonStatus;

    /// <summary>
    /// 总列表
    /// </summary>
    public ObservableCollection<CustomView> _customViews { get; set; }

    /// <summary>
    /// 添加弹窗内的图标列表
    /// </summary>
    public ObservableCollection<CustomViewIcon> CustomViewIconList { get; } = new()
    {
        new() { Name = "fa-filter", Icon = "\uf0b0" },
        new() { Name = "fa-folder", Icon = "\uf07b" },
        new() { Name = "fa-image", Icon = "\uf03e" },
        new() { Name = "fa-film", Icon = "\uf008" },
        new() { Name = "fa-music", Icon = "\uf001" },
        new() { Name = "fa-file-lines", Icon = "\uf15c" },
        new() { Name = "fa-file-zipper", Icon = "\uf1c6" },
        new() { Name = "fa-file-word", Icon = "\uf1c2" },
        new() { Name = "fa-file-excel", Icon = "\uf1c3" },
        new() { Name = "fa-file-pdf", Icon = "\uf1c1" },
        new() { Name = "fa-code", Icon = "\uf1c9" },
        new() { Name = "fa-database", Icon = "\uf1c0" },
        new() { Name = "fa-star", Icon = "\uf005" },
        new() { Name = "fa-heart", Icon = "\uf004" },
        new() { Name = "fa-tag", Icon = "\uf02b" },
        new() { Name = "fa-bookmark", Icon = "\uf02e" },
        new() { Name = "fa-plane", Icon = "\uf072" },
        new() { Name = "fa-gamepad", Icon = "\uf11b" },
        new() { Name = "fa-book", Icon = "\uf02d" }
    };

    private readonly ConstantResourceService _constantResourceService;
    private readonly WebApiService _webApiService;
    [ObservableProperty] private bool _isShowCustomViewEdit = false;
    public CustomViewEditViewModel()
    {
    }

    public CustomViewEditViewModel(ConstantResourceService constantResourceService, WebApiService webApiService)
    {
        _constantResourceService = constantResourceService;
        _webApiService = webApiService;
        //处理编辑添加模式转换
        WeakReferenceMessenger.Default.Register<DialogMessage>(this,
            (recipient, message) =>
            {
                if (message.Name == "CustomViewEdit")
                {
                    IsShowCustomViewEdit = message.IsShow;
                }
                if (message is { Name: "CustomViewEdit", IsShow: true})
                {
                    CustomViewMessage par = message.parameter as CustomViewMessage;
                    IsEdit = par.IsEdit;
                    _customViews = par.CustomViews;
                    if (par.IsEdit)
                    {
                        TempCustomView = par.CustomView;
                        CustomViewIconList.FirstOrDefault(x => x.Icon == par.CustomView.Icon)?.IsChecked = true;
                        SaveButtonStatus = new ButtonStatus("保存修改");
                    }
                    else
                    {
                        SaveButtonStatus = new ButtonStatus("确定添加");
                        TempCustomView = new CustomView();
                        CustomViewIconList[0].IsChecked = true;
                    }
                }

                if (message.IsShow == false)
                {
                    TempCustomView = null;
                    SaveButtonStatus = null;
                }
            });
    }

    [RelayCommand]
    private void CloseCustomView()
    {
        IsShowCustomViewEdit = false;
    }

    /// <summary>
    /// 视图类型切换
    /// </summary>
    [RelayCommand]
    private void ViewTypeChanged(string type)
    {
        int t = int.Parse(type);
        TempCustomView.Type = t;
        if (t == 0)
        {
            Title = "包含关键字";
            InputTitle = "多关键字用逗号隔开 (例如: .pdf, 报告) 不超过20个";
        }

        if (t == 1)
        {
            Title = "目标文件夹路径";
            InputTitle = "请输入绝对路径，例如: /图片/二次元";
        }
    }

    [RelayCommand]
    private async Task SaveCustomView()
    {
        //表单验证
        if(!_webApiService.FormValidation(TempCustomView.GetValidationResult(),Home.GlobalToastManager)) return;
        SaveButtonStatus.Begin("保存中");
        var obj = CustomViewIconList.FirstOrDefault(x => x.IsChecked);
        TempCustomView.Icon = obj.Icon;
        TempCustomView.Color = obj.Color;
        var isNewView = !IsEdit;
        var editedView = TempCustomView;
        var pluginView = Services.Plugins.PluginDtoMapper.View(editedView);
        var viewsToSave = _customViews.ToList();
        if (isNewView && !viewsToSave.Contains(editedView))
        {
            viewsToSave.Add(editedView);
        }
        var info = await _webApiService.UserApi.UpdateUserViewAsync(viewsToSave.Select(x => new CustomViewDto
        {
            Name = x.Name, Keywords = x.Keywords, Icon = _constantResourceService.GetSidebarClassName(x.Icon),
            Type = x.Type,
        }).ToArray());


        if (info.Status == 0)
        {
            if (isNewView)
            {
                if (!_customViews.Contains(editedView)) _customViews.Add(editedView);
                Services.Plugins.PluginEventHub.Publish(new() { Id = Drive.Plugin.Abi.DriveEventId.ViewAdded, View = pluginView });
            }
            CloseCustomView();
            Home.GlobalToastManager?.Show(
                new Toast(info.Msg),
                type: NotificationType.Success
            );
            return;
        }
        else
        {
            Home.GlobalToastManager?.Show(
                new Toast($"错误:{info.Msg}"),
                type: NotificationType.Warning
            );
        }


        SaveButtonStatus.End();
    }
}
