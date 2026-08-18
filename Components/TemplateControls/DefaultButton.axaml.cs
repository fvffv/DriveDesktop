using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace drive_desktop.Components.TemplateControls;

public class DefaultButton : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DefaultButton, string>(
            nameof(Text), 
            defaultValue: "按 钮"); 

    /// <summary>
    /// 提示文本
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<DefaultButton, string>(
            nameof(Icon), 
            defaultValue: "\uf0e0"); 

    /// <summary>
    /// 提示文本
    /// </summary>
    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<DefaultButton, bool>(nameof(IsLoading), defaultValue: false);
    /// <summary>
    /// 是否正在执行command命令
    /// </summary>
    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }
    // 1. 注册 Command 属性
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<DefaultButton, ICommand?>(
            nameof(ActionCommand), 
            defaultValue: null); // 默认值给 null 即可

    // 属性包装器
    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
    
    
    public static readonly StyledProperty<object?> ActionCommandParameterProperty =
        AvaloniaProperty.Register<DefaultButton, object?>(
            nameof(ActionCommandParameter), 
            defaultValue: null);

    public object? ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }

}