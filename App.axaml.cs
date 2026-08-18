using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using drive_desktop.ViewModels;
using drive_desktop.Views;
using System.Linq;

namespace drive_desktop
{
    public partial class App : Application
    {
        // 声明一个全局的 DI 容器实例
        public static Composition AppComposition { get; } = new Composition();
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = AppComposition.RootViewModel,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}