using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace drive_desktop.Components.TemplateControls;

public class DefaultInput : TemplatedControl
{
    public static readonly StyledProperty<double> IconFontSizeProperty =
        AvaloniaProperty.Register<DefaultInput, double>(
            nameof(IconFontSize), 
            defaultValue: 16);

    /// <summary>
    /// 图标大小
    /// </summary>
    public double IconFontSize
    {
        get => GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }
    public static readonly StyledProperty<IBrush> IconColorProperty =
        AvaloniaProperty.Register<DefaultInput, IBrush>(
            nameof(IconColor), 
            defaultValue: Brushes.Black);

    /// <summary>
    /// 图标颜色
    /// </summary>
    public IBrush IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    

    public static readonly StyledProperty<string> IconTextProperty =
        AvaloniaProperty.Register<DefaultInput, string>(
            nameof(IconText), 
            defaultValue: "");

    /// <summary>
    /// 用于图标文本
    /// </summary>
    public string IconText
    {
        get => GetValue(IconTextProperty);
        set => SetValue(IconTextProperty, value);
    }
    public static readonly StyledProperty<bool> AcceptsReturnProperty =
        AvaloniaProperty.Register<DefaultInput, bool>(
            nameof(AcceptsReturn), 
            defaultValue: false);

    /// <summary>
    /// 是否隐藏图标
    /// </summary>
    public bool AcceptsReturn
    {
        get => GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }
    public static readonly StyledProperty<bool> IsShowIconProperty =
        AvaloniaProperty.Register<DefaultInput, bool>(
            nameof(IsShowIcon), 
            defaultValue: false);

    /// <summary>
    /// 是否隐藏图标
    /// </summary>
    public bool IsShowIcon
    {
        get => GetValue(IsShowIconProperty);
        set => SetValue(IsShowIconProperty, value);
    }
    
    public static readonly StyledProperty<IBrush> BorderColorProperty =
        AvaloniaProperty.Register<DefaultInput, IBrush>(
            nameof(BorderColor), 
            defaultValue: Brushes.Transparent);

    /// <summary>
    /// 边框颜色
    /// </summary>
    public IBrush BorderColor
    {
        get => GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }
    
    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<DefaultInput, string>(
            nameof(Watermark), 
            defaultValue: "请输入内容..."); 

    /// <summary>
    /// 提示文本
    /// </summary>
    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DefaultInput, string>(
            nameof(Text), 
            defaultValue: "",
    defaultBindingMode: Avalonia.Data.BindingMode.TwoWay); 

    /// <summary>
    /// 编辑框内容
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    
    public static readonly StyledProperty<string> PasswordCharProperty =
        AvaloniaProperty.Register<DefaultInput, string>(
            nameof(PasswordChar), 
            defaultValue: ""); 

    /// <summary>
    /// 密码遮盖
    /// </summary>
    public string PasswordChar
    {
        get => GetValue(PasswordCharProperty);
        set => SetValue(PasswordCharProperty, value);
    }
}