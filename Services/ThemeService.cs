using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
namespace drive_desktop.Services;
public interface IThemeService
{
    ThemeVariant CurrentTheme { get; set; }
    void ToggleTheme();
}

public partial class ThemeService : ObservableObject, IThemeService
{
    private AppConfigService _appConfigService;
    
    [ObservableProperty]
    private ThemeVariant _currentTheme = ThemeVariant.Light; // 默认浅色
    public record ThemeChangedMessage(ThemeVariant NewTheme);
    //当检测到主题发生变化 发送全局通知给颜色消费者
    partial void OnCurrentThemeChanged(ThemeVariant oldValue, ThemeVariant newValue)
    {
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(newValue));
        _appConfigService.Config.Theme = newValue == ThemeVariant.Light ? 0 : 1;
        _appConfigService.Save();
        
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = newValue;
        }
    }

    public ThemeService(AppConfigService appConfigService)
    {
        _appConfigService = appConfigService;
        CurrentTheme = _appConfigService.Config.Theme == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
        
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = CurrentTheme;
        }
    }
    
    public void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == ThemeVariant.Light
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        
    }

}
