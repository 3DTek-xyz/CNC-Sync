using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class SshAuthenticationModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            SshAuthenticationMode.Password => "Password",
            SshAuthenticationMode.PrivateKey => "Private Key",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
