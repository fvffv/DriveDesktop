using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using drive_desktop.ViewModels;
using drive_desktop.Views;
using System.Linq;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using drive_desktop.Components.UserControls;

namespace drive_desktop
{
    public partial class App : Application
    {
        private bool _pluginShutdownStarted;
        private bool _pluginShutdownCompleted;
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
                desktop.MainWindow = desktop.Args?.Contains("--plugins") == true
                    ? new Window { Title = "网盘插件管理", Width = 1280, Height = 850,
                        Content = new PluginApplications { DataContext = AppComposition.Plugins } }
                    : new MainWindow
                {
                    DataContext = AppComposition.RootViewModel,
                };
                Dispatcher.UIThread.Post(async () =>
                {
                    try { await AppComposition.Plugins.StartAsync(); }
                    catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
                });
                desktop.ShutdownRequested += async (_, args) =>
                {
                    if (_pluginShutdownCompleted) return;
                    args.Cancel = true;
                    if (_pluginShutdownStarted) return;
                    _pluginShutdownStarted = true;
                    try { await AppComposition.Plugins.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(12)); }
                    catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
                    finally { _pluginShutdownCompleted = true; desktop.Shutdown(); }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
