using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PortraitRetouchPlugin.Core;

namespace PortraitRetouchPlugin;

/// <summary>精修画布：等比预览、缩放平移、原图分屏以及归一化局部修复笔触。</summary>
internal sealed class RetouchCanvas : Control, IDisposable
{
    private Bitmap? _before, _after;
    private double _zoom = 1, _split = .5;
    private Vector _offset;
    private Point? _dragPoint, _cursor;
    private bool _divider, _painting;
    private readonly List<HealSpot> _stroke = new();
    private bool _compare, _original, _brush;
    private float _radius = .012f;
    /// <summary>完成一次笔触后通知窗口提交可撤销编辑。</summary>
    public event EventHandler<HealSpot[]>? StrokeCompleted;
    /// <summary>缩放状态变化后通知窗口更新倍率显示。</summary>
    public event EventHandler? ZoomChanged;
    /// <summary>是否显示可拖动的原图/效果分屏。</summary>
    public bool Compare { get { return _compare; } set { _compare = value; InvalidateVisual(); } }
    /// <summary>按住原图按钮时临时显示原图。</summary>
    public bool Original { get { return _original; } set { _original = value; InvalidateVisual(); } }
    /// <summary>是否启用局部修复画笔。</summary>
    public bool Brush { get { return _brush; } set { _brush = value; InvalidateVisual(); } }
    /// <summary>修复半径，相对于图片短边。</summary>
    public float BrushRadius { get { return _radius; } set { _radius = value; InvalidateVisual(); } }
    /// <summary>相对于适应窗口的预览倍率。</summary>
    public double Zoom { get { return _zoom; } }

    /// <summary>替换原图预览，清理旧图片并恢复适应窗口状态。</summary>
    internal void SetSource(Bitmap before)
    {
        _before?.Dispose(); _after?.Dispose(); _before = before; _after = null; Fit();
    }
    /// <summary>替换处理结果，保留当前缩放、平移和对比分隔位置。</summary>
    internal void SetResult(Bitmap after)
    {
        _after?.Dispose(); _after = after; InvalidateVisual();
    }
    /// <summary>将图片完整显示在画布内。</summary>
    internal void Fit()
    {
        _zoom = 1; _offset = default; InvalidateVisual(); ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>调整预览倍率；放大后可拖动图片查看局部。</summary>
    internal void ChangeZoom(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 1, 6); if (_zoom == 1) _offset = default;
        InvalidateVisual(); ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>计算缩放和平移后的图片实际绘制矩形。</summary>
    private Rect ImageRect()
    {
        if (_before is null) return default;
        var scale = Math.Min(Bounds.Width / _before.Size.Width, Bounds.Height / _before.Size.Height) * _zoom;
        var width = _before.Size.Width * scale; var height = _before.Size.Height * scale;
        return new Rect((Bounds.Width - width) / 2 + _offset.X, (Bounds.Height - height) / 2 + _offset.Y, width, height);
    }
    /// <summary>绘制原图或效果，在分屏时裁剪绘制区域并显示拖拽分隔线。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#131413")), new Rect(Bounds.Size));
        if (_before is null) return;
        var box = ImageRect();
        using var clip = context.PushClip(new Rect(Bounds.Size));
        context.DrawImage(_original ? _before : _after ?? _before, new Rect(_before.Size), box);
        if (_compare && !_original && !_brush)
        {
            var split = Bounds.Width * _split;
            using (context.PushClip(new Rect(0, 0, split, Bounds.Height))) context.DrawImage(_before, new Rect(_before.Size), box);
            var ink = new SolidColorBrush(Color.Parse("#E8D9BB"));
            context.DrawLine(new Pen(ink, 1), new Point(split, 0), new Point(split, Bounds.Height));
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#242522")), new Pen(ink, 1), new Point(split, Bounds.Height * .72), 14, 14);
            context.DrawLine(new Pen(ink, 1.5), new Point(split - 4, Bounds.Height * .72 - 4), new Point(split - 4, Bounds.Height * .72 + 4));
            context.DrawLine(new Pen(ink, 1.5), new Point(split + 4, Bounds.Height * .72 - 4), new Point(split + 4, Bounds.Height * .72 + 4));
        }
        if (_brush && _cursor is Point cursor && box.Contains(cursor))
        {
            var radius = _radius * Math.Min(box.Width, box.Height);
            context.DrawEllipse(null, new Pen(Brushes.White, 1), cursor, radius, radius);
            context.DrawEllipse(null, new Pen(Brushes.Black, 1), cursor, radius + 1, radius + 1);
        }
    }
    /// <summary>开始拖动分隔线、平移或局部修复；所有坐标只来自当前图片区域。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_before is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (_brush && ImageRect().Contains(point)) { _painting = true; _stroke.Clear(); AddSpot(point); }
        else if (_compare && Math.Abs(point.X - Bounds.Width * _split) < 18) _divider = true;
        else _dragPoint = point;
        e.Pointer.Capture(this); e.Handled = true;
    }
    /// <summary>更新分屏位置或平移，画笔按间隔记录点以避免重复叠加。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this); _cursor = point;
        if (_painting) AddSpot(point);
        else if (_divider) _split = Math.Clamp(point.X / Math.Max(1, Bounds.Width), .02, .98);
        else if (_dragPoint is Point prior && _zoom > 1)
        {
            _offset += point - prior; _dragPoint = point;
            var box = ImageRect();
            _offset = new Vector(Math.Clamp(_offset.X, -Math.Max(0, (box.Width - Bounds.Width) / 2), Math.Max(0, (box.Width - Bounds.Width) / 2)),
                Math.Clamp(_offset.Y, -Math.Max(0, (box.Height - Bounds.Height) / 2), Math.Max(0, (box.Height - Bounds.Height) / 2)));
        }
        InvalidateVisual();
    }
    /// <summary>在笔触结束时一次性提交，使一次拖拽对应一次撤销。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var points = _painting ? _stroke.ToArray() : [];
        _painting = _divider = false; _dragPoint = null; _stroke.Clear(); e.Pointer.Capture(null);
        if (points.Length > 0) StrokeCompleted?.Invoke(this, points);
    }
    /// <summary>捕获丢失时取消未完成笔触。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); _painting = _divider = false; _dragPoint = null; _stroke.Clear();
    }
    /// <summary>鼠标离开后隐藏画笔圆圈。</summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e); _cursor = null; InvalidateVisual();
    }
    /// <summary>滚轮缩放预览。</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e); if (_before is null) return;
        ChangeZoom(e.Delta.Y > 0 ? 1.2 : 1 / 1.2); e.Handled = true;
    }
    /// <summary>把屏幕位置映射到图片归一化坐标，并限制单次笔触长度。</summary>
    private void AddSpot(Point point)
    {
        var box = ImageRect(); if (!box.Contains(point) || _stroke.Count >= 300) return;
        var spot = new HealSpot((float)((point.X - box.X) / box.Width), (float)((point.Y - box.Y) / box.Height), _radius);
        if (_stroke.Count > 0)
        {
            var prior = _stroke[^1]; var dx = (spot.X - prior.X) * box.Width; var dy = (spot.Y - prior.Y) * box.Height;
            if (dx * dx + dy * dy < Math.Pow(_radius * Math.Min(box.Width, box.Height) * .5, 2)) return;
        }
        _stroke.Add(spot);
    }
    /// <summary>释放画布持有的预览位图。</summary>
    public void Dispose()
    {
        _before?.Dispose(); _after?.Dispose(); _before = _after = null;
    }
}
