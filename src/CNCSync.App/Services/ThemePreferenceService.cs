using Avalonia;
using Avalonia.Styling;
using CNCSync.Core.Configuration;

namespace CNCSync.App.Services;

public sealed class ThemePreferenceService : IThemePreferenceService
{
    public void Apply(AppThemePreference preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = preference switch
        {
            AppThemePreference.FollowSystem => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
    }
}
