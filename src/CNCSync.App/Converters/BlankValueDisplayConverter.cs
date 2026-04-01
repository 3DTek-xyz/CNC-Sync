using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CNCSync.App.Converters;

public sealed class BlankValueDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? "(None)" : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
