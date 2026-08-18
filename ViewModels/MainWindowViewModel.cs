using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Services;
using drive_desktop.Views;
using Refit;
using Ursa.Controls;
namespace drive_desktop.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public WindowToastManager? ToastManager { get; set; }
        /// <summary>
        /// 界面主题
        /// </summary>
        [ObservableProperty] private ThemeVariant _currentTheme = ThemeVariant.Light;

        /// <summary>
        /// 主题切换组件的vm
        /// </summary>
        public ThemeSwitchViewModel ThemeSwitchVM { get; }
        //懒加载homevm
        private readonly Func<HomeViewModel> _homeVmFactory;
        /// <summary>
        /// 登录注册界面index
        /// </summary>
        [ObservableProperty] private int _currentPageIndex = 0;
        /// <summary>
        /// 登录表单
        /// </summary>
        public LoginInfoModel LoginInfoModel { get;} = new LoginInfoModel();
        /// <summary>
        /// 注册表单
        /// </summary>
        public UserRegInfoModel  UserRegInfoModel { get; } = new UserRegInfoModel();
        /// <summary>
        /// 登录按照状态
        /// </summary>
        public ButtonStatus LoginButtonStatus { get;} = new ButtonStatus("登 录");
        public ButtonStatus RegButtonStatus { get;} = new ButtonStatus("注 册");
        public ButtonStatus CodeButtonStatus { get;} = new ButtonStatus("获取验证码");
        /// <summary>
        /// 主题切换服务
        /// </summary>
        private readonly IThemeService _themeService;
        private AppConfigService _appConfigService;
        private WebApiService _webApiService;
        private UserInfoService _userInfoService;
        /// <summary>
        /// 切换页面
        /// </summary>
        [RelayCommand]
        private void TogglePage()
        {
           
            CurrentPageIndex = CurrentPageIndex == 0 ? 1 : 0;
        }


        
        public MainWindowViewModel(UserInfoService userInfoService,IThemeService themeService,
            ThemeSwitchViewModel themeSwitchVM,AppConfigService appConfigService,WebApiService webApiService,Func<HomeViewModel> homeVmFactory)
        {
            _appConfigService=appConfigService;
            _themeService = themeService;
            CurrentTheme = themeService.CurrentTheme;
            ThemeSwitchVM = themeSwitchVM;
            _homeVmFactory = homeVmFactory;
            _webApiService = webApiService;
            _userInfoService = userInfoService;

            

           LoginInfoModel.UsernameOrEmail = _appConfigService.Config.UserName;
           LoginInfoModel.Password = _appConfigService.Config.Password;

           
            //注册监听到主题色发生变化后的消费者
            WeakReferenceMessenger.Default.Register<ThemeService.ThemeChangedMessage>(this,
                (recipient, message) => { CurrentTheme = message.NewTheme; });
        }

        public MainWindowViewModel()
        {
            // 这里可以随便写点假数据供预览器显示
        }
        
        /// <summary>
        /// 发送验证码
        /// </summary>
        [RelayCommand]
        private async Task SendCaptcha()
        {
            if (string.IsNullOrEmpty(UserRegInfoModel.Email))
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast("邮箱不能为空！"), 
                    type: NotificationType.Warning
                );
                return;
            }
            if (!new EmailAddressAttribute().IsValid(UserRegInfoModel.Email))
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast("邮箱格式错误！"), 
                    type: NotificationType.Warning
                );
                return;
            }
            CodeButtonStatus.Begin("发送中");
            var info =await _webApiService.UserApi.SeedEmailCode(UserRegInfoModel.Email);
            if (info.Status == 0)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast("发送成功！"), 
                    type: NotificationType.Success
                );
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(1000);
                    CodeButtonStatus.Begin($"验证码({60-i})");
                }
                CodeButtonStatus.End();
            }
            else
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast($"发送失败: {info.Msg}"), 
                    type: NotificationType.Error
                );
            }
            RegButtonStatus.End();
            
        }

        [RelayCommand]
        private async Task Register()
        {
            if(!_webApiService.FormValidation(UserRegInfoModel.GetValidationResult(),MainWindow.GlobalToastManager)) return;
                
            RegButtonStatus.Begin("注册中");
            var info =await _webApiService.UserApi.RegisterUserAsync(UserRegInfoModel);
            if (info.Status == 0)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast("注册成功！"), 
                    type: NotificationType.Success
                );
                CurrentPageIndex = 0;
            }
            else
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast($"注册失败: {info.Msg}"), 
                    type: NotificationType.Error
                );
            }
            RegButtonStatus.End();
        }
        /// <summary>
        /// 登录
        /// </summary>
        [RelayCommand]
        private async Task Login()
        {
            
           
           //表单验证
            if(!_webApiService.FormValidation(LoginInfoModel.GetValidationResult(),MainWindow.GlobalToastManager)) return;
            
            LoginButtonStatus.Begin("登陆中");
            try
            {
                LoginInfo request = LoginInfoModel;

                var info =
                    await _webApiService.UserApi.LoginAsync(request);

                if (info.Status != 0)
                {
                    MainWindow.GlobalToastManager?.Show(
                        new Toast($"登录失败：{info.Msg}"),
                        type: NotificationType.Error);

                    return;
                }

                if (info.Data is null)
                {
                    throw new InvalidOperationException(
                        "登录成功，但服务器没有返回登录数据。");
                }

                _appConfigService.Config.UserName = LoginInfoModel.UsernameOrEmail;
                _appConfigService.Config.Password = LoginInfoModel.Password;
                _appConfigService.Config.JWT = info.Data.token;
                _appConfigService.Save();

                await _userInfoService.ReUserInfo();

                var cloudInfo =
                    await _webApiService.FileApi.GetCloudInfoAsync();

                if (cloudInfo.Status == 0)
                {
                    _userInfoService.CloudInfo = cloudInfo.Data;
                }

                var homeVm = _homeVmFactory();

                WeakReferenceMessenger.Default.Send(
                    new OpenWindowMessage("Home", homeVm));
            }
            catch (ApiException exception)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast(
                        $"登录请求失败：HTTP {(int)exception.StatusCode} " +
                        $"{exception.StatusCode}\n{exception.Content}"),
                    type: NotificationType.Error);
            }
            catch (HttpRequestException exception)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast($"无法连接服务器：{exception.Message}"),
                    type: NotificationType.Error);
            }
            catch (TaskCanceledException)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast("登录请求超时"),
                    type: NotificationType.Error);
            }
            catch (Exception exception)
            {
                MainWindow.GlobalToastManager?.Show(
                    new Toast($"登录失败：{exception.Message}"),
                    type: NotificationType.Error);
            }
            finally
            {
                LoginButtonStatus.End();
            }
    
        }
    }
}