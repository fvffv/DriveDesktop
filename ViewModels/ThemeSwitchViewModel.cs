using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using drive_desktop.Services;
using CommunityToolkit.Mvvm.Messaging;


namespace drive_desktop.ViewModels;

public partial class ThemeSwitchViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isLight = true;
    
   
    [RelayCommand]
    private void switchTheme()
    {
        _themeService.ToggleTheme();
    }

    /// <summary>
    /// 主题切换服务
    /// </summary>
    private readonly IThemeService _themeService;
    public ThemeSwitchViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        
        IsLight = _themeService.CurrentTheme == ThemeVariant.Light;
        //注册监听到主题色发生变化后的消费者
        WeakReferenceMessenger.Default.Register<ThemeService.ThemeChangedMessage>(this, (recipient, message) =>
        {
            IsLight = message.NewTheme == ThemeVariant.Light ;
        });
    }



    public ThemeSwitchViewModel()
    {
        
    }
}