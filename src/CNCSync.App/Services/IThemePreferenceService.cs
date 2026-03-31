using CNCSync.Core.Configuration;

namespace CNCSync.App.Services;

public interface IThemePreferenceService
{
    void Apply(AppThemePreference preference);
}
