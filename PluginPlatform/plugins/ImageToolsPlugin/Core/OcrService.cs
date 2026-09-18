using RapidOcrNet;
using SkiaSharp;

namespace ImageToolsPlugin.Core;

/// <summary>一段识别文字及其在图像中的矩形位置。</summary>
internal sealed record RecognizedLine(string Text, float X, float Y, float Width, float Height);

/// <summary>延迟加载的本地中英文 OCR；实例只由一个后台任务使用，不发送图片到网络。</summary>
internal sealed class OcrService : IDisposable
{
    private RapidOcr? _engine;

    /// <summary>识别当前编辑版本，按段报告进度；取消令牌传递到 ONNX 推理。</summary>
    internal RecognizedLine[] Recognize(SKBitmap bitmap, IProgress<(int Completed, int Total)>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_engine is null)
        {
            var root = Path.Combine(Path.GetDirectoryName(typeof(OcrService).Assembly.Location)!, "Models");
            var engine = new RapidOcr();
            try
            {
                engine.InitModels(Path.Combine(root, "detection.onnx"), Path.Combine(root, "orientation.onnx"),
                    Path.Combine(root, "recognition.onnx"), Path.Combine(root, "dictionary.txt"), Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
                _engine = engine;
            }
            catch { engine.Dispose(); throw; }
        }
        var result = _engine.Detect(bitmap, RapidOcrOptions.Default, progress, token);
        return result.TextBlocks.Select(block =>
        {
            var left = block.BoxPoints.Min(p => p.X); var top = block.BoxPoints.Min(p => p.Y);
            return new RecognizedLine(block.Text, left, top, block.BoxPoints.Max(p => p.X) - left, block.BoxPoints.Max(p => p.Y) - top);
        }).Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
    }

    /// <summary>按垂直重叠聚合同一行，再按水平位置排序，供 Excel 组织单元格。</summary>
    internal static RecognizedLine[][] GroupRows(IEnumerable<RecognizedLine> lines)
    {
        var rows = new List<List<RecognizedLine>>();
        foreach (var line in lines.OrderBy(line => line.Y + line.Height / 2))
        {
            var row = rows.LastOrDefault();
            if (row is null || Math.Abs(row.Average(x => x.Y + x.Height / 2) - (line.Y + line.Height / 2)) >
                Math.Max(4, Math.Min(row.Average(x => x.Height), line.Height) * 0.55))
            {
                row = new List<RecognizedLine>(); rows.Add(row);
            }
            row.Add(line);
        }
        return rows.Select(row => row.OrderBy(line => line.X).ToArray()).ToArray();
    }

    /// <summary>释放模型和原生推理资源；必须等待当前任务结束后调用。</summary>
    public void Dispose()
    {
        _engine?.Dispose(); _engine = null;
    }
}
