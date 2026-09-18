using SkiaSharp;

namespace ImageToolsPlugin.Core;

/// <summary>暂存图片及其编辑版本；原始数据只读，导出和裁剪不修改源文件。</summary>
internal sealed class ImageAsset
{
    /// <summary>创建一个已经过格式和像素数检查的图片条目。</summary>
    internal ImageAsset(string name, string originalPath, int width, int height, long bytes, string source)
    {
        Name = name; OriginalPath = originalPath; WorkingPath = originalPath;
        Width = width; Height = height; Bytes = bytes; Source = source;
    }
    public string Name { get; }
    public string Source { get; }
    public string OriginalPath { get; }
    public string WorkingPath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; }
    public string Detail { get { return $"{Source} · {Width} × {Height}"; } }
}

/// <summary>只在后台执行的图片解码、方向校正、裁剪、缩放和编码工具。</summary>
internal static class ImageProcessor
{
    internal const long MaxInputBytes = 128L * 1024 * 1024;
    internal const long MaxPixels = 32_000_000;

    /// <summary>检查图片头后解码首帧，并将相机方向信息应用到像素。</summary>
    internal static SKBitmap Decode(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
        using var stream = new SKManagedStream(input);
        using var codec = SKCodec.Create(stream) ?? throw new IOException("无法识别这张图片，请使用 PNG、JPEG、WebP、BMP、GIF 或 ICO。");
        if (codec.Info.Width < 1 || codec.Info.Height < 1 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new IOException("图片像素过大，当前支持不超过 3200 万像素的图片。");
        using var raw = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(raw.Info, raw.GetPixels()) != SKCodecResult.Success) throw new IOException("图片内容不完整或已损坏。");
        var w = raw.Width; var h = raw.Height;
        var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var bitmap = new SKBitmap(rotated ? h : w, rotated ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            var matrix = codec.EncodedOrigin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
                _ => SKMatrix.Identity
            };
            canvas.SetMatrix(matrix); canvas.DrawBitmap(raw, 0, 0);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    /// <summary>复制输入流到插件临时目录，限制体积并支持取消；失败删除未完成文件。</summary>
    internal static async Task CopyInputAsync(Stream input, string target, CancellationToken token)
    {
        try
        {
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            var buffer = new byte[65536]; long total = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, token);
                if (count == 0) break;
                total += count;
                if (total > MaxInputBytes) throw new IOException("单张图片不能超过 128 MiB。");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
        }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
    }

    /// <summary>按实际像素坐标裁剪，区域必须完整位于图片内部。</summary>
    internal static SKBitmap Crop(SKBitmap source, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || (long)x + width > source.Width || (long)y + height > source.Height)
            throw new ArgumentException("裁剪区域超出图片范围。");
        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height), new SKRect(0, 0, width, height));
        return result;
    }

    /// <summary>按最长边等比缩小图片，0 表示原尺寸，不放大原图。</summary>
    internal static SKBitmap Resize(SKBitmap source, int maxSide)
    {
        var factor = maxSide > 0 ? Math.Min(1d, maxSide / (double)Math.Max(source.Width, source.Height)) : 1d;
        var result = new SKBitmap(Math.Max(1, (int)Math.Round(source.Width * factor)), Math.Max(1, (int)Math.Round(source.Height * factor)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, result.Width, result.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return result;
    }

    /// <summary>编码 PNG、JPEG 或 WebP；JPEG 用白色替代透明背景。</summary>
    internal static byte[] Encode(SKBitmap bitmap, string format, int quality)
    {
        var kind = format.ToLowerInvariant() switch
        {
            "png" => SKEncodedImageFormat.Png,
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            _ => throw new ArgumentException("请选择 PNG、JPEG 或 WebP。")
        };
        using var flattened = kind == SKEncodedImageFormat.Jpeg ? new SKBitmap(bitmap.Width, bitmap.Height) : null;
        if (flattened is not null)
        {
            using var canvas = new SKCanvas(flattened);
            canvas.Clear(SKColors.White); canvas.DrawBitmap(bitmap, 0, 0);
        }
        using var image = SKImage.FromBitmap(flattened ?? bitmap);
        using var encoded = image.Encode(kind, Math.Clamp(quality, 1, 100)) ?? throw new IOException("当前环境不支持这个图片编码器。");
        return encoded.ToArray();
    }

    /// <summary>在输出目录生成安全文件名；清除文件系统保留字符和路径部分。</summary>
    internal static string SafeName(string name)
    {
        var file = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
        var clean = new string(file.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (string.IsNullOrEmpty(clean)) clean = "图片";
        var stem = clean.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             "123456789¹²³".Contains(stem[3]))) clean = "_" + clean;
        return clean[..Math.Min(clean.Length, 100)];
    }

    /// <summary>先写入临时文件，完成后原子提交；同名输出自动追加序号，取消不留下半成品。</summary>
    internal static string SaveOutput(string directory, string name, string extension, Action<string> write, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new IOException("请选择有效的绝对输出目录。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".image-tools-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            token.ThrowIfCancellationRequested();
            write(temporary);
            token.ThrowIfCancellationRequested();
            for (var index = 0; index < 10000; index++)
            {
                var target = Path.Combine(directory, SafeName(name) + (index == 0 ? "" : $" ({index})") + extension);
                try { File.Move(temporary, target, false); return target; }
                catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
            }
            throw new IOException("输出目录存在过多同名文件。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
