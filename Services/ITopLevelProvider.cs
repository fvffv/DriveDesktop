using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace drive_desktop.Services;

public interface ITopLevelProvider
{
    TopLevel? GetTopLevel();
}

// 实现服务 用于返回剪贴版对象
public class TopLevelProvider : ITopLevelProvider
{
    public TopLevel? GetTopLevel()
    {
        // 如果是桌面端应用 (Windows/macOS/Linux)
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        
        // 如果是移动端或浏览器端 (Android/iOS/WebAssembly)
        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView);
        }

        return null;
    }
}