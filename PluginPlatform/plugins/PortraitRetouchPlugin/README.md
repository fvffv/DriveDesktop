# 人像精修 · PORTRAIT

普通 .NET 11 托管插件，ID `com.drive.plugins.portrait-retouch`。通过独立运行器运行，主程序保持 NativeAOT。界面采用独立的深色摄影工作台和暖金色控件，不跟随主程序切换亮色。

## 打开与编辑

- 在图片文件菜单点击“人像精修”，或从插件应用卡片进入。不会接管 FilePage 双击。一次编辑一张图片；菜单多选时打开第一张支持的图片。
- 支持本地图片和云端目录浏览、翻页选图。打开新图会替换当前编辑会话，先导出需要保留的作品。
- 内置“试用示例人像”，示例为 AI 生成的虚构成年人肖像，不含用户账户数据。
- 输入支持 PNG、JPEG、WebP、BMP、GIF、ICO；多帧图片处理首帧，EXIF 方向自动校正。单张最多 128 MiB、3200 万像素。

## 功能

| 范围 | 功能 |
| --- | --- |
| 人像 | 自然磨皮、肤色提亮、红润、瘦脸、大眼、亮眼、美齿 |
| 调色 | 曝光、对比度、高光、阴影、饱和度、冷暖、细节锐化、暗角 |
| 局部修复 | 调节原图像素单位的画笔直径，点按或涂抹小瑕疵，外环肤色采样与羽化填补 |
| 预设 | 原图、自然、清透、暖肤、质感；缩略图由当前图片实际计算生成 |
| 对比 | 拖动分屏线、原图切换、按住空格查看原图、滚轮缩放与拖动平移 |
| 编辑历史 | 最多 60 步撤销/重做，Ctrl+Z / Ctrl+Y，重置也可撤销 |
| 导出 | 按原尺寸保存 JPEG、PNG、WebP，自定义画质和目录，同名自动加序号 |

人脸选择框可以限定某一张人脸或全部已定位人脸（最多 8 张，按面积排序）。调色始终影响全图。未检测到人脸时，人像美颜滑块禁用，调色和局部修复仍可使用。

预览最长边 1400 像素；倍率相对于“适应窗口”，并非原图像素 1:1。导出会用相同参数重新处理原图，默认保存到网盘配置的下载目录。原图不会被覆盖，取消删除未完成的输出临时文件。

修复笔触位于形变之后的图片坐标；先调好瘦脸、大眼，再进行局部修复，能更容易准确定位瑕疵。一张图最多 2000 个修复采样点。JPEG 会将透明区域填白。

## ONNX 的作用与效果边界

随插件部署 MIT 许可的 YuNet `face_detection_yunet_2023mar.onnx`，约 228 KiB。ONNX Runtime 在本机 CPU 上预测人脸框和五个关键点，不识别人物身份，也不上传图片。

美颜算法结合人脸框、双眼和嘴部保护区、双颊肤色采样，执行边缘感知磨皮、局部颜色调整和柔边形变。它是轻量自然精修，不是生成式人脸重建，也不是逐像素皮肤分割。侧脸、大角度倾斜、遮挡、浓妆、复杂光线或多张脸相互重叠时，定位及局部遮罩可能不准确；请降低强度并用对比检查。牙齿与眼白提亮会筛选局部低饱和亮部，但不能保证完美分割。局部修复适合小瑕疵，不适合移除大物体。

## 构建与部署

已加入 `drive-desktop.slnx` 和 `PluginPlatform/Deploy-Plugins.targets`，主程序构建、发布时自动部署到 `Plugins/PortraitRetouchPlugin`。只携带目标系统对应的 ONNX 原生依赖，公共 .NET、Avalonia、SkiaSharp 使用运行器提供的版本。

独立发布（在仓库根目录执行）：

```powershell
dotnet publish PluginPlatform/plugins/PortraitRetouchPlugin/PortraitRetouchPlugin.csproj -c Release -r win-x64 --self-contained false -o artifacts/PortraitRetouchPlugin
```

更换 RID 可为其他平台生成包；必须部署整个文件夹，保留 `.deps.json`、模型、原生库、许可证。当前验证环境为 Windows x64；Linux/macOS 尚未设备实测，并需使用对应平台运行器及 ONNX Runtime 支持的架构。

模型 SHA-256：`8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`。

模型来源：<https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet>。许可证见 `Licenses/YuNet.txt`，其他依赖见 `THIRD-PARTY-NOTICES.txt`。
