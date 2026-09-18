# DrivePluginApp

这是面向 .NET 11、Drive.Plugin.SDK 2.1.2 的普通托管 Avalonia 插件。宿主通过独立的 Drive.Plugin.Runner 加载插件，运行器提供 .NET、Avalonia 和 UI 线程。

## 开发入口

- `Plugin.cs`：插件名称、作者、简介、版本、ICO 图标、背景色、标签、权限及生命周期。`OnAppActivated` 对应插件应用卡片的打开按钮。
- `Plugin.Events.cs`：全部 29 个宿主事件的订阅、退订和具名处理方法，与 `Plugin.cs` 组成同一个插件类。方法均带中文 XML 注释、权限及数据字段说明，直接在对应方法体中编写业务代码。
- `MainWindow.axaml`：插件界面。明暗主题由运行器跟随宿主同步。
- `MainWindow.axaml.cs`：界面事件示例，通过 `Drive.User.GetCurrentAsync` 读取账户。窗口关闭时取消查询。
- `IconData.cs`：插件 ICO 图标的 Base64 数据。

模板使用 `PluginWindowHost.ShowNearHostAsync` 复用窗口并按宿主位置居中；Windows 下关联宿主窗口所有者，使插件位于宿主前方。Linux/macOS 使用 Avalonia 定位与激活，最终位置由窗口管理器决定。停用会取消未完成的打开请求并关闭窗口，重新启用后可以再次打开。

## 宿主事件

`OnLoad`、`OnEnable`、`OnDisable`、`OnShutdown` 是插件生命周期方法，`OnAppActivated` 是应用卡片入口。目录变化、传输完成等通知通过 `Drive.Events` 订阅，不需要重写 SDK 基类。

| 分类 | 事件 | 订阅权限 |
| --- | --- | --- |
| 应用、主题 | ApplicationStarted、ApplicationStopping、ThemeChanged | 无额外权限 |
| 账户、视图 | UserChanged、ViewAdded、ViewDeleted | UserRead |
| 目录、文件 | FolderChanged、FileCreated、FolderCreated、FileRenamed、FilesMoved、FilesDeleted、FileCopied、FileOpened、FileDoubleClicked、SelectionChanged | FileRead |
| 搜索、分享 | FileSearched、ShareLinkCreated、ShareLinkUpdated、ShareLinkDeleted | FileRead |
| 上传 | UploadStarted、FileUploaded、UploadFailed、UploadCancelled | UploadRead |
| 下载 | DownloadStarted、FileDownloaded、DownloadFailed、DownloadCancelled | DownloadRead |
| 菜单点击 | ActionInvoked | UiMenu、UiFileMenu、UiFolderMenu 中至少一项 |

模板在 `OnLoad` 中调用 `SubscribeEvents()`，**只订阅已声明权限对应的事件**。例如需要目录变化和下载完成通知，将 `Plugin.cs` 中权限声明改为：

```csharp
[PluginPermission(PluginPermission.UiApplication | PluginPermission.UserRead |
                  PluginPermission.FileRead | PluginPermission.DownloadRead)]
```

重新编译并在宿主授权后，在 `OnFolderChanged` 和 `OnFileDownloaded` 中编写处理逻辑即可。默认只声明 UiApplication 和 UserRead，因此文件、上传、下载和菜单处理方法虽已完整生成，但对应订阅暂不开启；不需要的处理器及订阅也可删除。

`ActionInvoked` 还需要先注册菜单；文件菜单和目录菜单注册分别需要 UiFileMenu + FileRead、UiFolderMenu + FileRead。菜单停用时会被移除，需在每次 `OnEnable` 时异步重新注册，不能同步等待宿主请求。

事件在停用时由 SDK 自动暂停，重新启用会恢复；模板仅在最终 `OnShutdown` 中退订，避免重复注册或重新启用后丢失通知。处理方法先记录一条示例日志，可自行删除。界面插件的处理器运行在 Runner UI 线程，耗时工作使用异步方法并捕获异常。双击事件仅通知，宿主默认打开继续执行；创建目录兼容地触发 FileCreated 和 FolderCreated，请勿重复执行业务。

## 编译与发布

首次使用时，需要将提供者交付的 `Drive.Plugin.Abi`、`Drive.Plugin.SDK`、`Drive.Plugin.Avalonia` 2.1.2 NuGet 包目录配置为包源。网盘项目的 `artifacts/plugin-packages` 就是本地包源；已配置 `DrivePluginLocal` 时无需重复添加。

在项目目录执行：

```powershell
dotnet build -c Release
dotnet publish -c Release --self-contained false -o ./publish
```

把 `publish` 的内容放入客户端 `Plugins/DrivePluginApp/` 目录，再在客户端授权并启用插件。插件文件夹可以包含私有依赖 DLL，宿主只识别带 `[DrivePlugin]` 的插件入口。

插件保持 `PublishAot=false`、`PublishTrimmed=false`、`PublishSingleFile=false`、`SelfContained=false`。自包含、压缩单文件及框架体积优化由 Runner 负责；不要把 Runner 的发布参数复制到插件类库。插件也不需要创建 `Application`、调用 `AppBuilder` 或自行启动另一套 UI 消息循环。

发布只部署程序和运行文件，自动排除 XML 注释文档、Markdown 说明、PDB 调试符号及 `.lib` / `.a` 等链接文件，并清理发布目录中这些类型的旧文件。`.deps.json`、`.runtimeconfig.json`、ONNX 模型、OCR 字典、配置、字体、许可证等仍保留，不按 EXE/DLL 后缀做白名单。编译目录和 SDK NuGet 包中的 XML 注释不受影响。发布目录必须独立于源码目录；如确有运行时 XML 文件，可在发布清单的对应 `ResolvedFileToPublish` 项上标记 `DriveKeepForPublish=true`。

Avalonia、SkiaSharp、HarfBuzzSharp、MicroCom.Runtime 和 Tmds.DBus.Protocol 等共享依赖由配套 Runner 提供，SDK 在构建和发布时排除它们的重复副本。插件自己的业务依赖仍正常发布。升级旧项目时更新 SDK 与 Drive.Plugin.Avalonia 引用，并同步更新 Runner；使用新的发布目录可避免旧版生成的共享 DLL 残留。

按功能申请所需的 `PluginPermission`；模板默认只申请应用入口与账户读取。新增文件菜单、下载或其他功能时需申请对应权限。SDK 包同时提供 XML 文档，可在 IDE 中查看各方法、参数和事件说明。
