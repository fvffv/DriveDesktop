using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace PortraitRetouchPlugin.Core;

/// <summary>人脸框和五个关键点，坐标均归一化到已校正方向的原图。</summary>
internal sealed record FaceRegion(SKRect Bounds, SKPoint LeftEye, SKPoint RightEye, SKPoint Nose,
    SKPoint MouthLeft, SKPoint MouthRight, float Score)
{
    /// <summary>人脸面积，用于优先显示较大的人脸。</summary>
    internal float Area { get { return Bounds.Width * Bounds.Height; } }
}

/// <summary>使用 YuNet ONNX 在本机定位人脸及五官；只做位置检测，不做人脸身份识别。</summary>
internal sealed class FaceDetector : IDisposable
{
    private InferenceSession? _session;

    /// <summary>等比缩放到 640 方形并在右下补黑，按 YuNet 的 BGR 0～255 输入约定执行推理。</summary>
    internal FaceRegion[] Detect(SKBitmap source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_session is null)
        {
            var model = Path.Combine(Path.GetDirectoryName(typeof(FaceDetector).Assembly.Location)!, "Models", "face_detection_yunet_2023mar.onnx");
            using var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1 };
            _session = new InferenceSession(model, options);
        }
        const int size = 640;
        var scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
        using var canvasBitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(SKColors.Black);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, new SKRect(0, 0, source.Width * scale, source.Height * scale), new SKSamplingOptions(SKFilterMode.Linear));
        }
        var pixels = canvasBitmap.GetPixelSpan();
        var data = new float[size * size * 3];
        for (var i = 0; i < size * size; i++)
        {
            data[i] = pixels[i * 4 + 2]; data[i + size * size] = pixels[i * 4 + 1]; data[i + size * size * 2] = pixels[i * 4];
        }
        using var run = new RunOptions();
        using var cancellation = token.Register(() => run.Terminate = true);
        using var output = _session.Run([NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.Single(), new DenseTensor<float>(data, [1, 3, size, size]))],
            _session.OutputMetadata.Keys.ToArray(), run);
        token.ThrowIfCancellationRequested();
        var maps = output.ToDictionary(x => x.Name, x => x.AsTensor<float>().ToArray());
        var candidates = new List<FaceRegion>();
        foreach (var stride in new[] { 8, 16, 32 })
        {
            var cols = size / stride;
            var cls = maps["cls_" + stride]; var obj = maps["obj_" + stride];
            var boxes = maps["bbox_" + stride]; var points = maps["kps_" + stride];
            for (var i = 0; i < cols * cols; i++)
            {
                var score = MathF.Sqrt(Math.Clamp(cls[i], 0, 1) * Math.Clamp(obj[i], 0, 1));
                if (score < .72f) continue;
                var cx = (i % cols + boxes[i * 4]) * stride;
                var cy = (i / cols + boxes[i * 4 + 1]) * stride;
                var w = MathF.Exp(boxes[i * 4 + 2]) * stride;
                var h = MathF.Exp(boxes[i * 4 + 3]) * stride;
                var nx = source.Width * scale; var ny = source.Height * scale;
                var bounds = new SKRect(Math.Clamp((cx - w / 2) / nx, 0, 1), Math.Clamp((cy - h / 2) / ny, 0, 1),
                    Math.Clamp((cx + w / 2) / nx, 0, 1), Math.Clamp((cy + h / 2) / ny, 0, 1));
                if (bounds.Width < .015f || bounds.Height < .015f) continue;
                SKPoint Point(int p)
                {
                    return new SKPoint((points[i * 10 + p * 2] + i % cols) * stride / nx,
                        (points[i * 10 + p * 2 + 1] + i / cols) * stride / ny);
                }
                var eye1 = Point(0); var eye2 = Point(1); var mouth1 = Point(3); var mouth2 = Point(4);
                candidates.Add(new(bounds, eye1.X < eye2.X ? eye1 : eye2, eye1.X < eye2.X ? eye2 : eye1,
                    Point(2), mouth1.X < mouth2.X ? mouth1 : mouth2, mouth1.X < mouth2.X ? mouth2 : mouth1, score));
            }
        }
        var kept = new List<FaceRegion>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Score))
        {
            if (kept.All(existing => Overlap(existing.Bounds, candidate.Bounds) < .3f)) kept.Add(candidate);
            if (kept.Count == 8) break;
        }
        return kept.OrderByDescending(x => x.Area).ToArray();
    }

    /// <summary>计算人脸框交并比，用于去除同一张人脸的重复预测。</summary>
    private static float Overlap(SKRect a, SKRect b)
    {
        var intersection = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
            Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return intersection / Math.Max(1e-8f, a.Width * a.Height + b.Width * b.Height - intersection);
    }

    /// <summary>在推理结束后释放 ONNX 会话。</summary>
    public void Dispose()
    {
        _session?.Dispose(); _session = null;
    }
}
