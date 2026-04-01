using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class AppThemePreferenceDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AppThemePreference preference
            ? preference switch
            {
                AppThemePreference.Light => "Light",
                AppThemePreference.FollowSystem => "Follow System",
                _ => preference.ToString()
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
