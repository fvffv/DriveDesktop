# 压缩包预览器

通过网盘 SDK 获取临时下载链接，以可寻址 HTTP Range 流读取 ZIP、7z、RAR 和未压缩 TAR 的目录和所需压缩数据。插件在普通托管运行器中运行，主程序可继续 NativeAOT 发布。

## 使用

1. 构建或发布主程序，插件自动出现在输出目录 `Plugins/ArchivePreviewPlugin`。
2. 在主程序插件页启用“压缩包预览器”。
3. 在主程序文件页双击 ZIP、7z、RAR 或 TAR 文件即可打开预览（后缀不区分大小写，主程序默认打开动作仍会执行）；也可从文件菜单选择“预览压缩包”，或从插件卡片打开窗口后选择当前网盘目录中的压缩包。
4. 双击文件夹进入；搜索匹配整个压缩包中的文件名称和路径。
5. 选择文件或目录，点击“下载选中文件”。文件保存到主程序当前配置的下载目录，保留包内路径；同名文件加 `(1)` 等后缀，不覆盖已有文件。

加密压缩包可输入密码后点击“重新读取”；密码只保存在当前窗口会话内，不写入缓存或日志。取消、关闭窗口、停用插件或切换账户会取消后台工作。已完成的文件保留，未完成 `.part` 文件清理。

目录列表使用内嵌的 Font Awesome 6 Free Solid 字体，按文件后缀显示文档、图片、音视频、代码和压缩文件图标；未知格式使用通用文件图标。文件夹配色适配明暗主题，无需安装字体或访问主程序资源。字体许可见 `Assets/Fonts/LICENSE.txt`。

UI 采用主程序的蓝色、圆角及 `HomeBg`、`SidebarBg`、边框、文字明暗配色，主题由运行器随主程序同步。Windows 窗口使用 SDK 2.0.4 的 `ShowNearHostAsync`：位于主窗口中央、关联原生所有者并激活，不全局置顶。Linux/macOS 共用 Avalonia UI，位置和激活仍由窗口管理器决定；本次运行验证环境为 Windows。

## 分段读取与缓存

- 初始只请求一个字节，检查 `206 Partial Content`、`Content-Range`、总长度和实体版本。
- 随解码器 Seek/Read 获取 64 KiB 分段；不预下载整个文件，也不预取后续块。
- 目录建立最多请求 16 MiB，最多接受 100,000 个条目；超过后停止，不静默退化成整包下载。
- 缓存位于 `Drive.System.GetInfoAsync().PluginTempDirectory/archive-preview-blocks-v1`。
- 按账户、账户根目录、文件 ID、内容哈希、修改时间、长度和强 ETag 隔离；缺乏可靠内容标识时不跨会话复用。
- 缓存最多 256 MiB，七天未使用的数据在后续打开或写入时清理；可手动清理。
- 临时链接和密码不落盘；401/403/410 或本项目 DownLoadKey 的 HTTP 200 JSON 过期错误会重新从 SDK 获取链接并重试一次，不读取错误响应体。远端长度或实体版本改变时终止当前读取。
- 缓存字节是原始压缩数据；小于一个块的压缩包可能完整落入一个块。缓存不是用户下载目录，清理缓存不会删除提取结果。

服务器必须支持 Range。若忽略 Range 返回 HTTP 200，立即关闭响应，不读取整个响应体。反向代理若禁止 Range，也无法实现此模式。

ZIP 通常只需要尾部目录、局部文件头与选中条目的压缩数据。7z、固实 RAR 可能需要先解码所选文件之前的压缩数据，因此提取量与选中条目的原始大小不一定相同，极端情况下可能涉及整个压缩块甚至整包。这由压缩格式决定，界面会提示。TAR.GZ、TGZ 等连续压缩格式、缺失分卷的压缩包和自解压 EXE 当前不支持。

普通 ZIP 显式验证 CRC32；加密 ZIP 通过 SharpZipLib 验证 AES 认证码并在适用时验证 CRC。路径穿越、绝对路径、保留设备名、符号链接/硬链接条目不提取；下载路径中的目录联接也会被拒绝。只有完整写入、检查大小及解码流正常结束后才显示为最终文件。批量部分失败时已完成的文件保留。

## 权限与构建

申请 `FileRead | DownloadRead | UserRead | UiFileMenu | UiApplication`。下载接口读取主程序实时配置；提取在插件进程执行，因此不会创建主程序原有下载任务，进度显示在本插件窗口。

独立发布：

```powershell
dotnet publish PluginPlatform/plugins/ArchivePreviewPlugin/ArchivePreviewPlugin.csproj -c Release -o artifacts/ArchivePreviewPlugin
```

复制整个输出文件夹到客户端 `Plugins` 目录。主程序自动部署由 `PluginPlatform/Deploy-Plugins.targets` 实现，构建依赖声明为 `ReferenceOutputAssembly=false`，隔离 AOT/裁剪/单文件属性。明确只做主程序隔离检查时可以传 `-p:SkipBundledPlugins=true`。

解压实现使用 [SharpCompress 0.50.4](https://github.com/adamhathcock/sharpcompress/tree/c083c6efd843a844b0c8f7878787360e815be781) 和 [SharpZipLib 1.4.2](https://github.com/icsharpcode/SharpZipLib/tree/v1.4.2)，均为 MIT 许可。Avalonia、.NET 运行时和 Drive SDK 由运行器提供；解压依赖由插件携带。程序按 `[DrivePlugin]` 元数据识别入口，依赖 DLL 不会作为插件单独启动。

关键校验位于 `PluginPlatform/Tests/ArchivePreviewChecks`，真实 NativeAOT/独立窗口检查位于 `PluginPlatform/Tests/SmokeHost/ArchiveUiChecks.cs`。检查使用本地样本及 SharpCompress 上游测试归档，不访问真实网盘账户。实际执行证据见 `docs/plugins/Validation.md`。
