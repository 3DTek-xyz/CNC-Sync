using System.Globalization;
using Avalonia.Data.Converters;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Converters;

public sealed class WatchProfileWorkItemModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WatchProfileWorkItemMode mode
            ? mode switch
            {
                WatchProfileWorkItemMode.ChangedFilesAndFolders => "Individual files and folders",
                WatchProfileWorkItemMode.TopLevelChildFolders => "Grouped project folders",
                _ => value?.ToString()
            }
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
