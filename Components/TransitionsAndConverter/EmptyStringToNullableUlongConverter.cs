using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace drive_desktop.Components.TransitionsAndConverter;

public class EmptyStringToNullableUlongConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (ulong.TryParse(text, NumberStyles.Integer, culture, out var result))
            return result;

        return BindingOperations.DoNothing;
    }
}