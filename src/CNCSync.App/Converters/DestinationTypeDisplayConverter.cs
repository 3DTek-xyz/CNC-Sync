using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class DestinationTypeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DestinationType.Ftp => "FTP",
            DestinationType.LocalFolder => "Local Folder",
            DestinationType.Sftp => "SFTP",
            DestinationType.Scp => "SCP",
            DestinationType.NetworkShare => "Network Share",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
