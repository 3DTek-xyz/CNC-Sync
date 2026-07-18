using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class ProcessingModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ProcessingMode mode
            ? mode switch
            {
                ProcessingMode.DefaultUpload => "Default Upload",
                ProcessingMode.ExternalScript => "External Script",
                ProcessingMode.ProCutApi => "ProCut Suite API",
                _ => mode.ToString()
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
