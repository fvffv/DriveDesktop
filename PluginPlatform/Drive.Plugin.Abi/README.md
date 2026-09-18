# Drive.Plugin.Abi

Drive 插件平台的公共协议定义包，供 `Drive.Plugin.SDK`、客户端与独立运行器共享。

## 包含内容

- `PluginPermission`：宿主 API 权限。
- `PluginCapabilities`、`PluginKind`：插件能力与类别。
- `HostOperation`、`DriveEventId`：宿主操作和事件编号。
- `PluginError`、`AbiVersions`：错误码、协议版本及消息限制。
- 为历史兼容保留的原生 ABI 结构；当前托管插件通过独立 Runner 与宿主通信，不使用这些结构加载 NativeAOT 插件 DLL。

## 使用方式

目标框架为 **.NET 11**，包版本为 **2.1.2**。普通插件只需安装 `Drive.Plugin.SDK`；带 Avalonia 界面的插件可以安装 `Drive.Plugin.Avalonia`，本包会作为传递依赖自动还原。

需要直接使用协议定义时：

```shell
dotnet add package Drive.Plugin.Abi --version 2.1.2
```

此包不包含运行器、图形界面或宿主 API 实现，需要配套的 Drive 客户端/Runner。开发时请保持平台各包的版本匹配。包内包含 DLL 和中文 XML 文档。

## 许可证

本包采用 **MIT** 许可证，完整文本随包附带于 `LICENSE.txt`。
