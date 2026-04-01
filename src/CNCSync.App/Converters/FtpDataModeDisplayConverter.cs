using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class FtpDataModeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is FtpDataMode mode
            ? mode switch
            {
                FtpDataMode.AutoPassive => "Auto Passive",
                FtpDataMode.Passive => "Passive",
                FtpDataMode.Active => "Active",
                _ => value?.ToString() ?? string.Empty
            }
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
