# Drive 插件平台 2.0

当前实现：NativeAOT 网盘主程序 + 普通 .NET 自包含插件运行器 + 普通托管插件 DLL。

- Drive.Plugin.Runner：独立进程，在进程主线程初始化 Avalonia，加载插件并管理窗口和生命周期。
- Drive.Plugin.SDK：保持强类型 API 和事件，底层通过本地管道连接主程序；NuGet 附带中文 XML 文档和公共依赖排除规则。
- Drive.Plugin.Hosting：NativeAOT 兼容的进程管理、权限检查、请求路由、日志和插件存储。
- Drive.Plugin.Avalonia：可选单窗口管理辅助类；不创建另一套 Application 或线程。
- Drive.Plugin.Abi：保留既有业务操作、权限和事件编号；当前 SDK 协议为 2.0，旧 C ABI 结构不再参与插件加载。
- Templates / Examples：托管插件模板和音乐示例。

1.x 原生导出生成器已退出当前方案。旧插件需使用 2.0 SDK 重新生成，不支持将已发布的 NativeAOT DLL 直接改成托管 DLL。

详见 ../docs/plugins/Plugin Development Guide.md。构建和发布客户端会自动将单文件运行器部署到主程序同一目录；可用 Publish-Runner.ps1 单独生成对应平台的自包含运行器。
