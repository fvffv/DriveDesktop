using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace drive_desktop.Components.TransitionsAndConverter;

/// <summary>
/// 自动单位转换
/// </summary>
public class FileSizeConverter : IValueConverter
{
    // 定义单位数组
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return FormatResult(0, 0, parameter, culture);

        if (!double.TryParse(value.ToString(), out double bytes))
        {
            return FormatResult(0, 0, parameter, culture);
        }

        if (bytes <= 0) return FormatResult(0, 0, parameter, culture);

        // 计算最合适的单位层级
        int unitIndex = 0;
        while (bytes >= 1024 && unitIndex < Units.Length - 1)
        {
            bytes /= 1024;
            unitIndex++;
        }

        return FormatResult(bytes, unitIndex, parameter, culture);
    }

    private static string FormatResult(
        double value,
        int unitIndex,
        object? parameter,
        CultureInfo culture)
    {
        var formattedValue = value.ToString("0.##", culture);

        return parameter?.ToString() switch
        {
            "Value" => formattedValue,
            "Unit" => Units[unitIndex],
            _ => $"{formattedValue} {Units[unitIndex]}"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
