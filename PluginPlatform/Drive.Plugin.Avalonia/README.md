# Drive.Plugin.Avalonia

为 Drive 托管插件提供 Avalonia 窗口管理辅助类 **`PluginWindowHost`**。插件可以使用 XAML 编写界面，复用已打开的窗口，并在停用时关闭界面。

插件界面运行在独立的 **Drive.Plugin.Runner** 进程中。Runner 初始化 Avalonia 和 UI 主线程，提供共享图形依赖并同步客户端主题。本包不包含运行器，也不将窗口嵌入 NativeAOT 主程序。

## 环境与安装

- **.NET 11**、`Drive.Plugin.SDK` **2.1.2**、Avalonia **12.1.2**。
- 当前开发环境使用 .NET 11 RC SDK；需要支持 `net11.0` 的开发工具及配套 Drive 客户端/Runner。
- SDK、Abi 和 Avalonia 编译依赖由 NuGet 自动还原。

```shell
dotnet add package Drive.Plugin.Avalonia --version 2.1.2
```

项目使用普通类库，启用插件发布规则：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <DrivePluginManaged>true</DrivePluginManaged>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <SelfContained>false</SelfContained>
    <PublishAot>false</PublishAot>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishSingleFile>false</PublishSingleFile>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Drive.Plugin.Avalonia" Version="2.1.2" />
  </ItemGroup>
</Project>
```

## 窗口入口示例

以下示例可直接编译；`CreateWindow` 中也可以改成返回自己的 XAML 窗口，如 `new MainWindow(Drive)`。不要自行创建 `Application`、调用 `AppBuilder` 或启动第二套 UI 消息循环。

```csharp
using Avalonia.Controls;
using Drive.Plugin.Abi;
using Drive.Plugin.Avalonia;
using Drive.Plugin.SDK;

/// <summary>支持重复打开和停用关闭的界面插件。</summary>
[DrivePlugin(Id = "com.example.hello.ui", Name = "Hello UI",
    Author = "插件作者", Version = "1.0.0",
    Capabilities = PluginCapabilities.HasUi | PluginCapabilities.WindowsUi |
                   PluginCapabilities.LinuxUi | PluginCapabilities.MacOsUi)]
[PluginPermission(PluginPermission.UiApplication)]
public sealed class HelloUiPlugin : DrivePlugin
{
    private PluginWindowHost? _windows;
    private CancellationTokenSource? _enabled;

    /// <summary>首次加载时创建窗口管理器。</summary>
    protected override void OnLoad()
    {
        _windows = new PluginWindowHost(Drive);
    }

    /// <summary>每次启用时开始一轮可取消的工作。</summary>
    protected override void OnEnable()
    {
        _enabled = new CancellationTokenSource();
    }

    /// <summary>用户点击插件卡片的打开按钮时显示窗口。</summary>
    protected override void OnAppActivated()
    {
        if (_enabled is not null)
        {
            _ = OpenWindowAsync(_enabled.Token);
        }
    }

    /// <summary>定位并打开窗口，处理停用取消及界面异常。</summary>
    private async Task OpenWindowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _windows!.ShowNearHostAsync(CreateWindow,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 停用取消属于正常生命周期。
        }
        catch (PluginException error) when (error.Error == PluginError.NotEnabled)
        {
            // 插件可能在读取宿主窗口信息时被停用。
        }
        catch (Exception error)
        {
            Drive.Logger.Error("打开窗口失败：" + error);
        }
    }

    /// <summary>由管理器在 UI 线程创建窗口；已有窗口会直接复用。</summary>
    private Window CreateWindow()
    {
        return new Window
        {
            Title = "Hello Drive",
            Width = 640,
            Height = 420,
            Content = new TextBlock { Text = "可以在这里换成自己的 XAML 界面。" }
        };
    }

    /// <summary>停用时取消请求并关闭窗口，下次启用仍可重新打开。</summary>
    protected override void OnDisable()
    {
        CancelAndClose();
    }

    /// <summary>退出时释放本地资源。</summary>
    protected override void OnShutdown()
    {
        CancelAndClose();
    }

    /// <summary>幂等地取消当前工作并关闭窗口。</summary>
    private void CancelAndClose()
    {
        _enabled?.Cancel();
        _enabled?.Dispose();
        _enabled = null;
        _windows?.CloseWindow();
    }
}
```

## 公开方法

| 方法 | 用途 |
| --- | --- |
| `Show` | 在 UI 线程创建或激活窗口，复用现有实例 |
| `ShowAsync` | 从任意线程调度显示窗口 |
| `ShowNearHostAsync` | 获取宿主窗口位置，居中显示并激活；支持取消 |
| `CloseWindow` | 在 UI 线程关闭窗口，允许后续重新打开 |
| `CloseWindowAsync` | 从任意线程调度关闭窗口 |
| `SetThemeAsync` | 设置当前插件进程的明暗主题；Runner 默认跟随宿主同步 |
| `ShutdownAsync` | 永久关闭管理器；普通停用应使用 `CloseWindow` 或 `CloseWindowAsync` |

生命周期、应用入口和界面插件的宿主事件由 Runner 调度到 UI 线程。耗时操作仍需异步执行；后台任务修改控件时须回到 UI 线程。包内 XML 文档提供中文方法、参数及异常说明。

## 跨平台与窗口位置

Windows、Linux、macOS 均使用对应平台的 Avalonia Runner。Windows 下 `ShowNearHostAsync` 还关联宿主窗口所有者，使插件窗口保持在宿主前方。Linux/macOS 使用 Avalonia 定位和激活，最终位置与焦点由窗口管理器决定；Wayland 等环境可能限制应用主动定位。

当前已在 Windows x64 验证窗口显示、停用关闭及重新启用。Linux/macOS 尚未实机验证。应使用匹配版本的 Runner 和 Avalonia；插件自己的原生依赖也须支持目标系统。

## 发布与部署

```shell
dotnet publish -c Release --self-contained false -o ./publish
```

将发布内容放入客户端的 `Plugins/你的插件名/`，在客户端授权并启用。自包含运行时、图形依赖、单文件和裁剪由 Runner 提供；插件无需重复部署共享 Avalonia、SkiaSharp、HarfBuzzSharp、MicroCom.Runtime 或 Tmds.DBus.Protocol。

SDK 的构建规则会保留插件私有依赖和必要资源，排除共享依赖及非运行用文档、调试符号和链接文件。保留 `.deps.json`、`.runtimeconfig.json` 和自己的模型/配置等资源。升级旧输出时建议使用新的发布目录。

## 许可证

本包采用 **MIT** 许可证，完整文本随包附带于 `LICENSE.txt`。Avalonia 及其他第三方依赖遵循各自的许可证。
