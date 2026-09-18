using SkiaSharp;

namespace PortraitRetouchPlugin.Core;

/// <summary>非破坏式精修流水线。每次从原图重算，预览和原尺寸导出使用相同算法。</summary>
internal static class RetouchEngine
{
    private static readonly float[] RangeWeights = Enumerable.Range(0, 766).Select(x => MathF.Exp(-x * x / (2f * 65 * 65))).ToArray();

    /// <summary>按人脸定位执行局部形变、美颜、全图调色及修复；返回独立位图，调用者负责释放。</summary>
    internal static SKBitmap Render(SKBitmap source, FaceRegion[] detected, RetouchSettings settings, HealSpot[] spots, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (settings == new RetouchSettings { FaceIndex = settings.FaceIndex } && spots.Length == 0) return source.Copy();
        var pixels = Raster.Read(source);
        var regions = detected.Where((_, i) => settings.FaceIndex < 0 || settings.FaceIndex == i)
            .Select(face => new Geometry(face, pixels)).ToArray();
        if ((settings.Slim > 0 || settings.Eyes > 0) && regions.Length > 0) pixels = Warp(pixels, regions, settings, token);
        var smooth = settings.Smooth > 0 || settings.Sharpness > 0 ? Smooth(pixels, token) : null;
        var output = new Raster(pixels.Width, pixels.Height, new byte[pixels.Bytes.Length]);
        var exposure = (float)Math.Pow(2, settings.Exposure);
        Rows(pixels.Height, token, y =>
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var i = (y * pixels.Width + x) * 4;
                var r = pixels.Bytes[i] / 255f; var g = pixels.Bytes[i + 1] / 255f; var b = pixels.Bytes[i + 2] / 255f;
                float skin = 0, cheek = 0, eyes = 0, mouth = 0;
                foreach (var region in regions)
                {
                    region.Masks(x, y, r, g, b, out var s, out var c, out var e, out var m);
                    skin = Math.Max(skin, s); cheek = Math.Max(cheek, c); eyes = Math.Max(eyes, e); mouth = Math.Max(mouth, m);
                }
                if (smooth is not null && settings.Smooth > 0)
                {
                    var amount = skin * (float)settings.Smooth / 100 * .72f;
                    r = Mix(r, smooth.Bytes[i] / 255f, amount); g = Mix(g, smooth.Bytes[i + 1] / 255f, amount); b = Mix(b, smooth.Bytes[i + 2] / 255f, amount);
                }
                var light = skin * (float)settings.Whiten / 100 * .22f;
                r += (1 - r) * light; g += (1 - g) * light; b += (1 - b) * light;
                var blush = cheek * skin * (float)settings.Blush / 100;
                r = Mix(r, Math.Min(1, r + .20f), blush * .55f); g *= 1 - blush * .06f;
                var lum = .2126f * r + .7152f * g + .0722f * b;
                var chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                var pale = Math.Clamp((.32f - chroma) / .22f, 0, 1) * Math.Clamp((lum - .25f) / .35f, 0, 1);
                var eyeAmount = eyes * pale * (float)settings.EyeLight / 100 * .28f;
                r += (1 - r) * eyeAmount; g += (1 - g) * eyeAmount; b += (1 - b) * eyeAmount;
                var tooth = mouth * pale * (float)settings.Teeth / 100 * .75f;
                r = Mix(r, lum, tooth * .5f); g = Mix(g, lum, tooth * .5f); b = Mix(b, lum, tooth * .5f);
                r += (1 - r) * tooth * .22f; g += (1 - g) * tooth * .22f; b += (1 - b) * tooth * .22f;
                if (smooth is not null && settings.Sharpness > 0)
                {
                    var amount = (float)settings.Sharpness / 100 * .8f;
                    r += (pixels.Bytes[i] - smooth.Bytes[i]) / 255f * amount;
                    g += (pixels.Bytes[i + 1] - smooth.Bytes[i + 1]) / 255f * amount;
                    b += (pixels.Bytes[i + 2] - smooth.Bytes[i + 2]) / 255f * amount;
                }
                r *= exposure; g *= exposure; b *= exposure;
                lum = Math.Clamp(.2126f * r + .7152f * g + .0722f * b, 0, 1);
                var tonal = (float)(settings.Shadows / 100) * (1 - lum) * (1 - lum) * .22f +
                    (float)(settings.Highlights / 100) * lum * lum * .22f;
                var contrast = 1 + (float)settings.Contrast / 100 * .7f;
                r = (r + tonal - .5f) * contrast + .5f; g = (g + tonal - .5f) * contrast + .5f; b = (b + tonal - .5f) * contrast + .5f;
                r += (float)settings.Warmth / 100 * .09f; b -= (float)settings.Warmth / 100 * .09f;
                lum = .2126f * r + .7152f * g + .0722f * b;
                var saturation = 1 + (float)settings.Saturation / 100;
                r = lum + (r - lum) * saturation; g = lum + (g - lum) * saturation; b = lum + (b - lum) * saturation;
                var dx = (x + .5f) / pixels.Width * 2 - 1; var dy = (y + .5f) / pixels.Height * 2 - 1;
                var vignette = 1 - Math.Clamp((dx * dx + dy * dy - .15f) / 1.85f, 0, 1) * (float)settings.Vignette / 100 * .65f;
                output.Bytes[i] = Channel(r * vignette); output.Bytes[i + 1] = Channel(g * vignette); output.Bytes[i + 2] = Channel(b * vignette);
                output.Bytes[i + 3] = pixels.Bytes[i + 3];
            }
        });
        foreach (var spot in spots) { token.ThrowIfCancellationRequested(); Heal(output, spot); }
        token.ThrowIfCancellationRequested();
        return output.ToBitmap();
    }

    /// <summary>用两次边缘感知的五点滤波平滑颜色，原始颜色差大的边缘不互相混合。</summary>
    private static Raster Smooth(Raster input, CancellationToken token)
    {
        var current = input;
        var radius = Math.Clamp((int)(Math.Min(input.Width, input.Height) * .007), 2, 24);
        var offsets = new[] { -radius, -Math.Max(1, radius / 2), 0, Math.Max(1, radius / 2), radius };
        var weights = new[] { .15f, .6f, 1f, .6f, .15f };
        for (var axis = 0; axis < 2; axis++)
        {
            var source = current;
            var result = new Raster(input.Width, input.Height, new byte[input.Bytes.Length]);
            var horizontal = axis == 0;
            Rows(input.Height, token, y =>
            {
                for (var x = 0; x < input.Width; x++)
                {
                    var at = (y * input.Width + x) * 4;
                    float r = 0, g = 0, b = 0, total = 0;
                    for (var k = 0; k < offsets.Length; k++)
                    {
                        var sx = horizontal ? Math.Clamp(x + offsets[k], 0, input.Width - 1) : x;
                        var sy = horizontal ? y : Math.Clamp(y + offsets[k], 0, input.Height - 1);
                        var sample = (sy * input.Width + sx) * 4;
                        var difference = Math.Abs(input.Bytes[at] - input.Bytes[sample]) + Math.Abs(input.Bytes[at + 1] - input.Bytes[sample + 1]) + Math.Abs(input.Bytes[at + 2] - input.Bytes[sample + 2]);
                        var weight = RangeWeights[difference] * weights[k];
                        r += source.Bytes[sample] * weight; g += source.Bytes[sample + 1] * weight; b += source.Bytes[sample + 2] * weight; total += weight;
                    }
                    result.Bytes[at] = (byte)(r / total); result.Bytes[at + 1] = (byte)(g / total); result.Bytes[at + 2] = (byte)(b / total);
                    result.Bytes[at + 3] = input.Bytes[at + 3];
                }
            });
            current = result;
        }
        return current;
    }

    /// <summary>局部逆向采样实现瘦脸和大眼，形变在作用范围边缘连续衰减为零。</summary>
    private static Raster Warp(Raster input, Geometry[] faces, RetouchSettings settings, CancellationToken token)
    {
        var output = new Raster(input.Width, input.Height, new byte[input.Bytes.Length]);
        Rows(input.Height, token, y =>
        {
            for (var x = 0; x < input.Width; x++)
            {
                float sx = x, sy = y;
                foreach (var face in faces)
                {
                    var dx = (sx - face.Cx) / (face.Width * .65f); var dy = (sy - face.Cy) / (face.Height * .62f);
                    var oval = Math.Max(0, 1 - dx * dx - dy * dy);
                    var lower = Math.Clamp((sy - face.EyeY) / Math.Max(1, face.Height * .22f), 0, 1);
                    sx += (sx - face.Cx) * (float)settings.Slim / 100 * .22f * oval * oval * lower;
                    foreach (var eye in face.Eyes)
                    {
                        var radius = Math.Max(2, face.EyeDistance * .36f);
                        var ex = sx - eye.X; var ey = sy - eye.Y;
                        var falloff = Math.Max(0, 1 - (ex * ex + ey * ey) / (radius * radius));
                        var amount = (float)settings.Eyes / 100 * .28f * falloff * falloff;
                        sx -= ex * amount; sy -= ey * amount;
                    }
                }
                input.Sample(sx, sy, output.Bytes, (y * input.Width + x) * 4);
            }
        });
        return output;
    }

    /// <summary>从笔触外环估计干净肤色，羽化填补小瑕疵；局部修复不会覆盖整张图片。</summary>
    private static void Heal(Raster pixels, HealSpot spot)
    {
        var cx = spot.X * pixels.Width; var cy = spot.Y * pixels.Height;
        var radius = Math.Clamp(spot.Radius * Math.Min(pixels.Width, pixels.Height), 1, 256);
        float red = 0, green = 0, blue = 0, count = 0;
        for (var n = 0; n < 32; n++)
        {
            var angle = n * MathF.PI / 16;
            var x = (int)MathF.Round(cx + MathF.Cos(angle) * radius * 1.6f);
            var y = (int)MathF.Round(cy + MathF.Sin(angle) * radius * 1.6f);
            if (x < 0 || y < 0 || x >= pixels.Width || y >= pixels.Height) continue;
            var i = (y * pixels.Width + x) * 4;
            if (pixels.Bytes[i + 3] < 128) continue;
            red += pixels.Bytes[i]; green += pixels.Bytes[i + 1]; blue += pixels.Bytes[i + 2]; count++;
        }
        if (count < 4) return;
        red /= count; green /= count; blue /= count;
        for (var y = Math.Max(0, (int)(cy - radius)); y <= Math.Min(pixels.Height - 1, (int)(cy + radius)); y++)
        for (var x = Math.Max(0, (int)(cx - radius)); x <= Math.Min(pixels.Width - 1, (int)(cx + radius)); x++)
        {
            var distance = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
            var t = Math.Clamp((1 - distance) * 1.7f, 0, 1); var amount = t * t * (3 - 2 * t);
            var i = (y * pixels.Width + x) * 4;
            pixels.Bytes[i] = (byte)Mix(pixels.Bytes[i], red, amount);
            pixels.Bytes[i + 1] = (byte)Mix(pixels.Bytes[i + 1], green, amount);
            pixels.Bytes[i + 2] = (byte)Mix(pixels.Bytes[i + 2], blue, amount);
        }
    }

    /// <summary>限制后台并行度，并在每一行检查取消。</summary>
    private static void Rows(int count, CancellationToken token, Action<int> action)
    {
        Parallel.For(0, count, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, action);
    }
    /// <summary>线性混合两个值。</summary>
    private static float Mix(float a, float b, float weight) { return a + (b - a) * weight; }
    /// <summary>把归一化颜色转换为字节。</summary>
    private static byte Channel(float value) { return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255); }

    /// <summary>一张人脸的像素坐标、肤色采样和柔边五官保护区。</summary>
    private sealed class Geometry
    {
        internal readonly float Cx, Cy, Width, Height, EyeY, EyeDistance;
        internal readonly SKPoint[] Eyes;
        private readonly SKPoint _mouth;
        private readonly float _mouthWidth, _cb, _cr;

        /// <summary>将归一化人脸坐标映射到当前分辨率，并采样双颊作为肤色参考。</summary>
        internal Geometry(FaceRegion face, Raster image)
        {
            Width = face.Bounds.Width * image.Width; Height = face.Bounds.Height * image.Height;
            Cx = face.Bounds.MidX * image.Width; Cy = face.Bounds.MidY * image.Height;
            Eyes = [new(face.LeftEye.X * image.Width, face.LeftEye.Y * image.Height), new(face.RightEye.X * image.Width, face.RightEye.Y * image.Height)];
            EyeY = (Eyes[0].Y + Eyes[1].Y) / 2; EyeDistance = Math.Max(2, SKPoint.Distance(Eyes[0], Eyes[1]));
            _mouth = new((face.MouthLeft.X + face.MouthRight.X) / 2 * image.Width, (face.MouthLeft.Y + face.MouthRight.Y) / 2 * image.Height);
            _mouthWidth = Math.Max(Width * .16f, (face.MouthRight.X - face.MouthLeft.X) * image.Width * .7f);
            float cb = 0, cr = 0;
            foreach (var side in new[] { -.22f, .22f })
            {
                var x = Math.Clamp((int)(Cx + Width * side), 0, image.Width - 1);
                var y = Math.Clamp((int)((face.Nose.Y * image.Height + _mouth.Y) / 2), 0, image.Height - 1);
                var i = (y * image.Width + x) * 4;
                Chroma(image.Bytes[i] / 255f, image.Bytes[i + 1] / 255f, image.Bytes[i + 2] / 255f, out var a, out var b);
                cb += a / 2; cr += b / 2;
            }
            _cb = cb; _cr = cr;
        }

        /// <summary>得到皮肤、双颊、眼睛、嘴部权重；未检测到的人脸外部保持零权重。</summary>
        internal void Masks(float x, float y, float r, float g, float b, out float skin, out float cheek, out float eyes, out float mouth)
        {
            skin = cheek = eyes = mouth = 0;
            var oval = Ellipse(x, y, Cx, Cy, Width * .51f, Height * .52f);
            if (oval == 0) return;
            foreach (var eye in Eyes) eyes = Math.Max(eyes, Ellipse(x, y, eye.X, eye.Y, EyeDistance * .29f, EyeDistance * .13f));
            mouth = Ellipse(x, y, _mouth.X, _mouth.Y + Height * .015f, _mouthWidth, Height * .052f);
            var eyeProtection = 0f;
            foreach (var eye in Eyes) eyeProtection = Math.Max(eyeProtection, Ellipse(x, y, eye.X, eye.Y - Height * .015f, EyeDistance * .40f, EyeDistance * .24f));
            var lipProtection = Ellipse(x, y, _mouth.X, _mouth.Y, _mouthWidth * 1.35f, Height * .085f);
            Chroma(r, g, b, out var cb, out var cr);
            var color = Math.Clamp(1 - ((cb - _cb) * (cb - _cb) + (cr - _cr) * (cr - _cr)) / .0225f, 0, 1);
            skin = oval * color * (1 - eyeProtection) * (1 - lipProtection);
            cheek = Math.Max(Ellipse(x, y, Cx - Width * .23f, Cy + Height * .08f, Width * .19f, Height * .14f),
                Ellipse(x, y, Cx + Width * .23f, Cy + Height * .08f, Width * .19f, Height * .14f));
        }
        /// <summary>生成边界连续的椭圆遮罩。</summary>
        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            var dx = (x - cx) / Math.Max(1, rx); var dy = (y - cy) / Math.Max(1, ry);
            var t = Math.Clamp((1 - dx * dx - dy * dy) * 2, 0, 1);
            return t * t * (3 - 2 * t);
        }
        /// <summary>提取亮度无关的色差，兼容不同明暗肤色。</summary>
        private static void Chroma(float r, float g, float b, out float cb, out float cr)
        {
            cb = -.168736f * r - .331264f * g + .5f * b;
            cr = .5f * r - .418688f * g - .081312f * b;
        }
    }

    /// <summary>直通 Alpha 的 RGBA 像素缓冲，处理结束再还原 Skia 预乘格式。</summary>
    private sealed record Raster(int Width, int Height, byte[] Bytes)
    {
        /// <summary>复制像素并解除颜色预乘，避免透明区域在调色后出现黑边。</summary>
        internal static Raster Read(SKBitmap bitmap)
        {
            var bytes = bitmap.GetPixelSpan().ToArray();
            for (var i = 0; i < bytes.Length; i += 4)
            {
                var alpha = bytes[i + 3];
                if (alpha is 0 or 255) continue;
                for (var c = 0; c < 3; c++) bytes[i + c] = (byte)Math.Min(255, (bytes[i + c] * 255 + alpha / 2) / alpha);
            }
            return new(bitmap.Width, bitmap.Height, bytes);
        }
        /// <summary>将处理结果复制为独立 Skia 位图。</summary>
        internal SKBitmap ToBitmap()
        {
            var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var span = bitmap.GetPixelSpan();
            for (var i = 0; i < Bytes.Length; i += 4)
            {
                var alpha = Bytes[i + 3];
                span[i] = (byte)((Bytes[i] * alpha + 127) / 255); span[i + 1] = (byte)((Bytes[i + 1] * alpha + 127) / 255);
                span[i + 2] = (byte)((Bytes[i + 2] * alpha + 127) / 255); span[i + 3] = alpha;
            }
            return bitmap;
        }
        /// <summary>用双线性插值读取形变后的坐标，并限制在图片边界内。</summary>
        internal void Sample(float x, float y, byte[] output, int target)
        {
            x = Math.Clamp(x, 0, Width - 1); y = Math.Clamp(y, 0, Height - 1);
            var x0 = (int)x; var y0 = (int)y; var x1 = Math.Min(x0 + 1, Width - 1); var y1 = Math.Min(y0 + 1, Height - 1);
            var fx = x - x0; var fy = y - y0;
            for (var c = 0; c < 4; c++)
            {
                var a = Mix(Bytes[(y0 * Width + x0) * 4 + c], Bytes[(y0 * Width + x1) * 4 + c], fx);
                var b = Mix(Bytes[(y1 * Width + x0) * 4 + c], Bytes[(y1 * Width + x1) * 4 + c], fx);
                output[target + c] = (byte)Math.Clamp((int)MathF.Round(Mix(a, b, fy)), 0, 255);
            }
        }
    }
}
