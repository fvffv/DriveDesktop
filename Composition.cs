using drive_desktop.Components.UserControls;
using drive_desktop.Services;
using drive_desktop.ViewModels;
using Pure.DI;

namespace drive_desktop;

// 必须是 partial 类，Pure.DI 会在背后为你生成这个类的具体实现代码
public partial class Composition
{
    // 这个方法永远不会在运行时被调用，它只是给 Pure.DI 的源码生成器看
    private void Setup() => DI.Setup(nameof(Composition))
        //主题服务
        .Bind<IThemeService>().As(Lifetime.Singleton).To<ThemeService>()
        //配置文件服务
        .Bind<AppConfigService>().As(Lifetime.Singleton).To<AppConfigService>()
        //切换主题组件的vm
        .Bind<ThemeSwitchViewModel>().As(Lifetime.Singleton).To<ThemeSwitchViewModel>()
        //侧边栏vm
        .Bind<SidebarViewModel>().As(Lifetime.Singleton).To<SidebarViewModel>()
        //顶栏
        .Bind<TopBarViewModel>().As(Lifetime.Singleton).To<TopBarViewModel>()
        //视图编辑弹窗
        .Bind<CustomViewEditViewModel>().As(Lifetime.Singleton).To<CustomViewEditViewModel>()
        .Bind<WebApiService>().As(Lifetime.Singleton).To<WebApiService>()
        //常量资源类
        .Bind<ConstantResourceService>().As(Lifetime.Singleton).To<ConstantResourceService>()
        // 用户全局资源
        .Bind<UserInfoService>().As(Lifetime.Singleton).To<UserInfoService>()
        // 用户全局资源
        .Bind<SearchViewModel>().As(Lifetime.Singleton).To<SearchViewModel>()
        //新建文件夹弹窗vm
        .Bind<NewFolderDialog>().As(Lifetime.Singleton).To<NewFolderDialog>()
        //文件预览vm
        .Bind<FilePageViewModel>().As(Lifetime.Singleton).To<FilePageViewModel>()
        //文件预览vm
        .Bind<FilePageViewModel>().As(Lifetime.Singleton).To<FilePageViewModel>()
        //移动文件弹窗vm
        .Bind<MoveFilesViewModel>().As(Lifetime.Singleton).To<MoveFilesViewModel>()
        //移动文件弹窗vm
        .Bind<ImageViewDialog>().As(Lifetime.Singleton).To<ImageViewDialog>()
        //分享文件弹窗vm
        .Bind<ShareFileDialogViewModel>().As(Lifetime.Singleton).To<ShareFileDialogViewModel>()
        //文件属性弹窗
        .Bind<FilePropertiesDialogViewModel>().As(Lifetime.Singleton).To<FilePropertiesDialogViewModel>()
        //文件属性弹窗
        .Bind<TextViewDialogViewModel>().As(Lifetime.Singleton).To<TextViewDialogViewModel>()
        //剪贴板
        .Bind<ITopLevelProvider>().As(Lifetime.Singleton).To<TopLevelProvider>()
        //搜索界面
        .Bind<SearchPageViewModel>().As(Lifetime.Singleton).To<SearchPageViewModel>()
        //文件传输界面
        .Bind<FileTransmissionViewModel>().As(Lifetime.Singleton).To<FileTransmissionViewModel>()
        //文件传输服务
        .Bind<FileTransmissionService>().As(Lifetime.Singleton).To<FileTransmissionService>()
        //文件分享管理界面
        .Bind<FileSharePageViewModel>().As(Lifetime.Singleton).To<FileSharePageViewModel>()
        //文档预览
        .Bind<DocumentViewDialogViewModel>().As(Lifetime.Singleton).To<DocumentViewDialogViewModel>()
        //统计看板
        .Bind<StatisticsDashboardPageViewModel>().As(Lifetime.Singleton).To<StatisticsDashboardPageViewModel>()
        //设置
        .Bind<SettingPageViewModel>().As(Lifetime.Singleton).To<SettingPageViewModel>()
        //Ai对话
        .Bind<AiChatViewModel>().As(Lifetime.Singleton).To<AiChatViewModel>()
        // 分享文件面板
        .Bind<ShareViewModel>().As(Lifetime.Transient).To<ShareViewModel>()
        //声明根节点
        .Bind<HomeViewModel>().As(Lifetime.Transient).To<HomeViewModel>()
        .Root<MainWindowViewModel>("RootViewModel");

}