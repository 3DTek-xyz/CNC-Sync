namespace CNCSync.Tests;

internal static class TestSecrets
{
    public static string ProCutApiKey => string.Join("_", "pcu", "test", "key");
    public static string ProCutApiSecret => string.Join("_", "pcu", "test", "secret");
    public static string LegacyProCutApiSecret => string.Join("_", "legacy", "pcu", "secret");
}
