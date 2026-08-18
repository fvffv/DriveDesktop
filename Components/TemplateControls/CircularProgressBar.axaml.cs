using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace drive_desktop.Components.TemplateControls;

public class CircularProgressBar : ContentControl
{
    //进度值
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircularProgressBar, double>(nameof(Value), 0);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    //最大值
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CircularProgressBar, double>(nameof(Maximum), 100);

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    //圆环粗细
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CircularProgressBar, double>(nameof(StrokeThickness), 6);

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    //进度条颜色
    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<CircularProgressBar, IBrush?>(nameof(ProgressBrush));

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    //底环颜色
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CircularProgressBar, IBrush?>(nameof(TrackBrush));

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    //内部计算的圆弧角度
    public static readonly StyledProperty<double> SweepAngleProperty =
        AvaloniaProperty.Register<CircularProgressBar, double>(nameof(SweepAngle), 0);

    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        private set => SetValue(SweepAngleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 当 Value 或 Maximum 变化时，重新计算圆弧角度
        if (change.Property == ValueProperty || change.Property == MaximumProperty)
        {
            UpdateSweepAngle();
        }
    }

    private void UpdateSweepAngle()
    {
        double val = Math.Max(0, Math.Min(Value, Maximum));
        double max = Math.Max(0.0001, Maximum); // 防止除 0 异常
        SweepAngle = (val / max) * 360.0;
    }
}