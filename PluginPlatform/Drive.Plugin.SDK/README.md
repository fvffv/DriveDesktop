# Drive.Plugin.SDK

为 Drive 网盘客户端开发普通 C# 托管插件。通过强类型 API 访问网盘文件、上传下载队列、账户、菜单、存储和日志，并订阅宿主事件。

插件运行在独立的 **Drive.Plugin.Runner** 进程中，通过本地管道与 NativeAOT 主程序通信。此包是插件开发 SDK；运行时需要配套的 Drive 客户端和 Runner，不是任意 .NET 应用安装后即可使用的通用网盘服务。

## 环境与安装

- 目标框架：**.NET 11**。当前项目使用 .NET 11 RC SDK；开发时需安装支持 `net11.0` 的 SDK。
- 客户端、Runner 和插件应使用匹配的 SDK 版本；本包版本为 **2.1.2**。
- `Drive.Plugin.Abi` 由 NuGet 自动作为依赖安装；需要 XAML 界面时，再安装 `Drive.Plugin.Avalonia`。

```shell
dotnet add package Drive.Plugin.SDK --version 2.1.2
```

插件项目使用普通类库配置：

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
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Drive.Plugin.SDK" Version="2.1.2" />
  </ItemGroup>
</Project>
```

## 最小插件

每个插件 DLL 只声明一个公开、非抽象、非泛型、具有公开无参构造函数的入口类。构造函数中不能使用 `Drive`；在 `OnLoad` 开始时，上下文已经初始化。

```csharp
using Drive.Plugin.SDK;

/// <summary>记录客户端启动通知的示例插件。</summary>
[DrivePlugin(Id = "com.example.hello", Name = "Hello Drive",
    Author = "插件作者", Version = "1.0.0", Description = "最小日志插件")]
public sealed class HelloPlugin : DrivePlugin
{
    /// <summary>首次加载时订阅一次宿主事件。</summary>
    protected override void OnLoad()
    {
        Drive.Events.ApplicationStarted += OnApplicationStarted;
    }

    /// <summary>每轮启用时记录日志。</summary>
    protected override void OnEnable()
    {
        Drive.Logger.Info("插件已启用");
    }

    /// <summary>收到当前插件的应用启动通知。</summary>
    private void OnApplicationStarted(object? sender, DriveEvent args)
    {
        Drive.Logger.Info("收到应用启动通知");
    }

    /// <summary>最终退出时解除本地订阅。</summary>
    protected override void OnShutdown()
    {
        Drive.Events.ApplicationStarted -= OnApplicationStarted;
    }
}
```

插件名称、作者、简介、版本、ICO 图标、背景色和标签通过 `[DrivePlugin]` 声明。`IconBase64` 为 ICO 原始字节的 Base64 字符串，留空表示无图标。

## 宿主 API

从插件基类的 `Drive` 属性访问下列接口。方法、参数、权限和事件载荷的详细说明随包提供中文 XML 文档，可在 IDE 中查看。

| 入口 | 提供的功能 |
| --- | --- |
| `Drive.Files` | 文件和目录查询、重命名、移动、删除、复制、临时下载链接及小文件读取 |
| `Drive.Uploads` | 上传任务创建、查询、暂停、恢复和取消 |
| `Drive.Downloads` | 下载任务创建、查询、控制；`GetSaveDirectoryAsync` 获取客户端配置的下载目录 |
| `Drive.User` | 当前账户公开资料、容量信息 |
| `Drive.UI` | 通知、确认框、菜单注册、导航和文件预览 |
| `Drive.Storage` | 按插件 ID 隔离的持久化键值存储 |
| `Drive.System` | 宿主版本、系统、语言、主题、窗口信息及临时目录 |
| `Drive.Logger` | 带时间、等级及插件身份的结构化日志 |
| `Drive.Events` | 29 个宿主事件 |

需要受控能力时，在入口类上添加权限，例如：

```csharp
[PluginPermission(Drive.Plugin.Abi.PluginPermission.FileRead |
                  Drive.Plugin.Abi.PluginPermission.DownloadRead)]
```

声明权限后仍需用户在客户端授权。`PluginPermission.All` 可以申请全部现有权限；插件通常应只声明实际需要的权限。文件菜单使用 `RegisterFileMenuAsync`，目录菜单使用 `RegisterFolderMenuAsync`，两者分别需要 `UiFileMenu + FileRead`、`UiFolderMenu + FileRead`。

## 全部宿主事件

| 分类 | 事件 |
| --- | --- |
| 应用与主题 | `ApplicationStarted`、`ApplicationStopping`、`ThemeChanged` |
| 账户与视图 | `UserChanged`、`ViewAdded`、`ViewDeleted` |
| 文件与目录 | `FolderChanged`、`FileCreated`、`FolderCreated`、`FileRenamed`、`FilesMoved`、`FilesDeleted`、`FileCopied`、`FileOpened`、`FileDoubleClicked`、`SelectionChanged` |
| 搜索与分享 | `FileSearched`、`ShareLinkCreated`、`ShareLinkUpdated`、`ShareLinkDeleted` |
| 上传 | `UploadStarted`、`FileUploaded`、`UploadFailed`、`UploadCancelled` |
| 下载 | `DownloadStarted`、`FileDownloaded`、`DownloadFailed`、`DownloadCancelled` |
| 菜单 | `ActionInvoked` |

事件覆盖客户端观察到的操作，不是服务器全量变更流。双击事件只通知插件，客户端默认打开行为仍会执行；创建文件夹会兼容地触发 `FileCreated` 和 `FolderCreated`，避免重复处理。

在 `OnLoad` 中订阅，在 `OnShutdown` 中退订。停用时 SDK 暂停事件投递，重新启用时恢复；不要在每次启用时重复订阅。菜单在停用时会清除，需要在每次 `OnEnable` 中重新注册。

生命周期和事件回调应尽快返回，不要同步等待宿主异步请求。异步工作需处理取消和异常；停用后业务 API 已撤销，应取消本地任务、关闭窗口并释放资源。

## 发布与部署

```shell
dotnet publish -c Release --self-contained false -o ./publish
```

将整个发布目录部署到客户端的 `Plugins/你的插件名/`，再授权并启用。保留插件自己的依赖 DLL、JSON 依赖清单和所需资源；宿主通过 `[DrivePlugin]` 识别入口，不会把每一个依赖 DLL 都当作插件启动。

声明 `DrivePluginManaged=true` 后，包内构建规则会排除由 Runner 提供的 SDK、Avalonia、SkiaSharp、HarfBuzzSharp、MicroCom.Runtime 和 Tmds.DBus.Protocol 等共享依赖副本。还会从发布产物移除非运行用 XML/Markdown 文档、调试符号和链接文件；模型、字典、JSON、字体及许可证保留。开发时的 XML 注释不受影响。升级已有项目时，建议使用新的发布目录，避免旧共享 DLL 残留。

自包含、单文件和裁剪由 Runner 负责，插件 DLL 本身保持普通托管发布。插件可面向 Windows、Linux 和 macOS，需为目标系统提供相应 Runner；插件自身的原生依赖也须支持目标平台。当前运行验证覆盖 Windows x64。

## 许可证

本包采用 **MIT** 许可证，完整文本随包附带于 `LICENSE.txt`。第三方依赖遵循各自的许可证。
