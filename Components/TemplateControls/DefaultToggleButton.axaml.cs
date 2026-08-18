using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace drive_desktop.Components.TemplateControls;

public class DefaultToggleButton : ToggleButton
{
    
    
    public static readonly StyledProperty<double> IconFontSizeProperty =
        AvaloniaProperty.Register<DefaultToggleButton, double>(
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
        AvaloniaProperty.Register<DefaultToggleButton, IBrush>(
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
    
    
    
    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<DefaultToggleButton, double>(
            nameof(TitleFontSize), 
            defaultValue: 16);

    /// <summary>
    /// 标题大小
    /// </summary>
    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }
    public static readonly StyledProperty<IBrush> TitleColorProperty =
        AvaloniaProperty.Register<DefaultToggleButton, IBrush>(
            nameof(TitleColor), 
            defaultValue: Brushes.Black);

    /// <summary>
    /// 标题颜色
    /// </summary>
    public IBrush TitleColor
    {
        get => GetValue(TitleColorProperty);
        set => SetValue(TitleColorProperty, value);
    }
    
    
    public static readonly StyledProperty<string> IconTextProperty =
        AvaloniaProperty.Register<DefaultToggleButton, string>(
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
    
    
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DefaultToggleButton, string>(
            nameof(Title), 
            defaultValue: "默认文本");

    /// <summary>
    /// 用于图标文本
    /// </summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}