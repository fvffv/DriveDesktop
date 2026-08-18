using CommunityToolkit.Mvvm.ComponentModel;

namespace drive_desktop.Models;

public partial class ButtonStatus:ObservableObject
{   
    /// <summary>
    /// 是否加载状态
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = false;
    /// <summary>
    /// 标题
    /// </summary>
    [ObservableProperty]
    private string _title =  string.Empty;
    /// <summary>
    /// 是否启用按钮
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    private string _initTtile;
    public ButtonStatus(string title, bool isEnabled = true)
    {
        _title = title;
        _initTtile = title;
        _isEnabled = isEnabled;
    }


    public void Begin(string title)
    {
        Title = title;
        IsLoading = true;
        IsEnabled = false;
    }
    public void End()
    {
        Title = _initTtile;
        IsLoading = false;
        IsEnabled = true;
    }
}