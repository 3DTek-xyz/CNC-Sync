using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CBWSSSync.App.Converters;

public sealed class BooleanOpacityConverter : IValueConverter
{
    public static BooleanOpacityConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool flag && flag ? 0.55 : 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
