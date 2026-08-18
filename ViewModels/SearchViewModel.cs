using CommunityToolkit.Mvvm.ComponentModel;

namespace drive_desktop.ViewModels;

public partial class SearchViewModel :ViewModelBase
{
    [ObservableProperty]
    private bool _buttonEnabled = false;
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// 搜索文本内容变化 判断是不是空不是空 解除按钮禁止
    /// </summary>
    /// <param name="oldValue"></param>
    /// <param name="newValue"></param>
    partial void OnSearchTextChanged(string? oldValue, string? newValue)
    {
        if (string.IsNullOrEmpty(newValue))
        {
            ButtonEnabled = false;
        }
        else
        {
            ButtonEnabled = true;
        }
    }
}