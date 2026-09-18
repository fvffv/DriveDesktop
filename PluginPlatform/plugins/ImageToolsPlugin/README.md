# 图片处理插件

插件 ID：`com.drive.plugins.image-tools`。普通 .NET 11 托管插件，由独立的 `Drive.Plugin.Runner` 运行，共享运行器中的 .NET、Avalonia 和 SkiaSharp。

## 使用

1. 启用“图片处理”插件，在 FilePage 的图片文件菜单中点击“图片处理”；支持同时传入多张图片。网盘文件双击不会打开此插件。
2. 也可以在插件应用卡片中打开空窗口，再选择云端或本地图片。云端选择器从网盘当前目录开始，支持文件夹导航、多选、分页。
3. 在预览中拖拽框选，或展开“裁剪当前图片”输入像素坐标，然后应用裁剪。恢复原图只撤销本窗口的编辑。
4. 展开“压缩与格式设置”选择 PNG、JPEG、WebP、画质和最长边，点击“压缩 / 转换”。默认 JPEG、85% 画质、原尺寸；PNG 无损，不使用画质参数。JPEG 透明区域填白，输出体积不保证比原图更小。
5. PDF 每张图片一页；Word / Excel 可以切换“嵌入图片”和“识别为可编辑内容”。Excel 每图一张工作表，OCR 按文字位置组织行列，并非专业表格结构还原，复杂表格及数字需要核对。
6. “提取文字”识别当前图片的中英文，结果可复制或保存为 UTF-8 TXT。文档导出及格式转换可以选择全部图片或当前图片；左侧“上移”调整文档中的顺序。

导出默认使用主程序配置的下载目录，也可以更改目录。不会覆盖同名文件，会自动追加序号。原图仅复制到临时目录后处理；关闭窗口时清理本窗口的临时副本。取消保留已完成的输出，删除未完成的 `.part` 文件。

## 支持范围

- 输入 PNG、JPEG、WebP、BMP、GIF、ICO；动画/多帧输入处理首帧。应用 EXIF 方向后预览、裁剪及导出。
- 单张输入最大 128 MiB、3200 万像素，一个窗口最多 50 张图片。
- OCR 使用随插件部署的 PP-OCRv5 中英文模型，本地 CPU 推理，不向第三方上传图片。云端图片需先下载到临时目录。
- 云端传输 90 秒超时；过期临时链接自动重新申请一次。
- 窗口跟随宿主明暗主题，使用同款蓝色、圆角和 Font Awesome 图标，并通过 SDK 在宿主前居中打开。
- 代码使用跨平台组件。当前已验证 Windows x64；其他系统需要对应 RID 的运行器及 ONNX Runtime 原生库，尚未做设备实测。

## 构建与部署

项目已加入 `drive-desktop.slnx`，主程序构建/发布通过 `PluginPlatform/Deploy-Plugins.targets` 自动编译并复制到 `Plugins/ImageToolsPlugin`。

单独发布（在仓库根目录执行）：

```powershell
dotnet publish PluginPlatform/plugins/ImageToolsPlugin/ImageToolsPlugin.csproj -c Release -r win-x64 --self-contained false -o artifacts/ImageToolsPlugin
```

其他平台将 `win-x64` 换成对应 RID。主程序自动发布使用宿主 RID，只携带当前平台的 OCR 原生依赖。未指定 RID 的手动发布会包含 NuGet 提供的全部平台原生库。

部署时复制整个插件输出文件夹，包括 `Models`、原生库（可能位于根目录或 `runtimes`）、依赖 DLL、`.deps.json` 和许可证。不能只复制入口 DLL。宿主通过插件元数据识别 `ImageToolsPlugin.dll`，不会将普通依赖识别为插件。

插件保留 RapidOcrNet、ONNX Runtime、Open XML 等自身依赖，Avalonia/SkiaSharp 等公共依赖由 SDK 发布规则排除并从运行器共享。OCR 模型约 21.5 MiB，原生推理库额外占用磁盘空间；不要对动态加载的运行器或插件启用未验证的裁剪。

模型来源及校验见 `Models/README.md`，第三方声明见 `THIRD-PARTY-NOTICES.txt` 和 `Licenses`。
