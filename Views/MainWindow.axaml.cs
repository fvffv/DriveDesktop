using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using Ursa.Controls;

namespace drive_desktop.Views
{
    public partial class MainWindow : Window
    {
        public static WindowToastManager? GlobalToastManager { get; private set; }
        public MainWindow()
        {
            InitializeComponent();
            //窗口打开消息消费者
            WeakReferenceMessenger.Default.Register<OpenWindowMessage>(this, (r, m) =>
            {
                if (m.WindowName == "Home")
                {
                    var home = new Home();
                    home.DataContext = m.ViewModel;
                    home.Show();
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow = home;
                    }

                    home.Show();
                    this.Close(); 
                }
            });
        }
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            // 实例化 Manager，把当前 MainWindow 传进去作为宿主
            GlobalToastManager = new WindowToastManager(this)
            {
                MaxItems = 3, // 最多同时显示 3 个
                // 你可以通过 NotificationPosition 来控制它是从顶部还是右下角出来
                // Position = NotificationPosition.TopCenter 
            };
        }
        protected override void OnClosed(System.EventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);

            GlobalToastManager = null;

            DataContext = null;
            base.OnClosed(e);
        }
    }
}
