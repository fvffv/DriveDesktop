using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
namespace drive_desktop.Components.TransitionsAndConverter;

/// <summary>
/// 根据图标颜色自动生成背景色
/// </summary>
public class ColorToTransparentBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrWhiteSpace(colorString))
        {
            try
            {
                // 1. 解析原始颜色（支持 #RRGGBB, #AARRGGBB 甚至 Red 等名称）
                var color = Color.Parse(colorString);

                // 2. 替换透明度通道为 1A (十六进制 1A = 十进制 26)
                var transparentColor = Color.FromArgb(0x1A, color.R, color.G, color.B);

                // 3. 直接返回画刷对象，供 UI 的 Background / BorderBrush 绑定使用
                return new SolidColorBrush(transparentColor);
            }
            catch
            {
                // 解析失败时返回透明画刷，防止程序崩溃
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }   
}