using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageToolsPlugin;

/// <summary>等比图片预览和拖拽裁剪框；裁剪坐标始终使用原图像素，缩放不影响导出。</summary>
internal sealed class ImagePreview : Control, IDisposable
{
    private Bitmap? _bitmap;
    private int _pixelWidth, _pixelHeight;
    private Point? _start;
    private PixelRect _selection;
    /// <summary>用户拖拽选区后通知窗口更新像素坐标输入框。</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>当前裁剪选区，以原图像素为单位。</summary>
    public PixelRect Selection { get { return _selection; } }

    /// <summary>替换预览图片并释放旧位图，初始化全图选区。</summary>
    internal void SetImage(Bitmap? bitmap, int pixelWidth = 0, int pixelHeight = 0)
    {
        _bitmap?.Dispose(); _bitmap = bitmap;
        _pixelWidth = pixelWidth; _pixelHeight = pixelHeight;
        _selection = new PixelRect(0, 0, pixelWidth, pixelHeight);
        InvalidateVisual();
    }

    /// <summary>由坐标输入框更新选区，不重复触发编辑事件。</summary>
    internal void SetSelection(PixelRect selection)
    {
        _selection = selection; InvalidateVisual();
    }

    /// <summary>计算等比缩放后的图片显示区域。</summary>
    private Rect ImageRect()
    {
        if (_bitmap is null || _pixelWidth == 0 || _pixelHeight == 0) return default;
        var scale = Math.Max(0, Math.Min((Bounds.Width - 24) / _pixelWidth, (Bounds.Height - 24) / _pixelHeight));
        var width = _pixelWidth * scale; var height = _pixelHeight * scale;
        return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

    /// <summary>绘制图片及裁剪遮罩，控件尺寸变化时自动重新适配。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap is null) return;
        var box = ImageRect();
        if (box.Width <= 0 || box.Height <= 0) return;
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), box);
        if (_selection.Width < 1 || _selection.Height < 1 ||
            _selection == new PixelRect(0, 0, _pixelWidth, _pixelHeight)) return;
        var scale = box.Width / _pixelWidth;
        var selected = new Rect(box.X + _selection.X * scale, box.Y + _selection.Y * scale,
            _selection.Width * scale, _selection.Height * scale);
        var shade = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
        context.FillRectangle(shade, new Rect(box.X, box.Y, box.Width, selected.Y - box.Y));
        context.FillRectangle(shade, new Rect(box.X, selected.Bottom, box.Width, box.Bottom - selected.Bottom));
        context.FillRectangle(shade, new Rect(box.X, selected.Y, selected.X - box.X, selected.Height));
        context.FillRectangle(shade, new Rect(selected.Right, selected.Y, box.Right - selected.Right, selected.Height));
        context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 2), selected);
    }

    /// <summary>在图片内按下左键时开始框选并捕获鼠标。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_bitmap is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !ImageRect().Contains(e.GetPosition(this))) return;
        _start = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true;
    }

    /// <summary>拖拽中把屏幕坐标换算成原图像素，并将区域限制在图片内。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_start is not Point start) return;
        var box = ImageRect(); if (box.Width <= 0) return;
        var end = e.GetPosition(this);
        var x1 = Math.Clamp((int)((Math.Min(start.X, end.X) - box.X) * _pixelWidth / box.Width), 0, _pixelWidth - 1);
        var y1 = Math.Clamp((int)((Math.Min(start.Y, end.Y) - box.Y) * _pixelHeight / box.Height), 0, _pixelHeight - 1);
        var x2 = Math.Clamp((int)Math.Ceiling((Math.Max(start.X, end.X) - box.X) * _pixelWidth / box.Width), x1 + 1, _pixelWidth);
        var y2 = Math.Clamp((int)Math.Ceiling((Math.Max(start.Y, end.Y) - box.Y) * _pixelHeight / box.Height), y1 + 1, _pixelHeight);
        _selection = new PixelRect(x1, y1, x2 - x1, y2 - y1);
        SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }

    /// <summary>释放鼠标时结束框选。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); _start = null; e.Pointer.Capture(null);
    }

    /// <summary>鼠标捕获丢失时终止框选。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); _start = null;
    }

    /// <summary>释放最后一张预览图片。</summary>
    public void Dispose()
    {
        _bitmap?.Dispose(); _bitmap = null;
    }
}
