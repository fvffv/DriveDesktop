using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace drive_desktop.Components.TemplateControls;

//  定义一个伪类 :open，用于在 XAML 中触发打开动画
[PseudoClasses(":open")]
public class DefaultDialog : ContentControl
{
    // 定义依赖属性 IsOpen
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DefaultDialog, bool>(nameof(IsOpen), false);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    // 监听属性变化，动态添加或移除 :open 伪类
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == IsOpenProperty)
        {
            // 当 IsOpen 为 true 时，系统自动为其加上 :open 伪类，触发 XAML 中的动画
            PseudoClasses.Set(":open", change.GetNewValue<bool>());
        }
    }
}