using System.Security.Cryptography;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Core.Services;

internal static class ProcessingConfigurationFingerprint
{
    public static string Capture(ProcessingSetupSettings setup, ProCutApiSettings api) => setup.Mode switch
    {
        ProcessingMode.ProCutApi => Hash(new
        {
            setup.Mode,
            api.BaseUrl,
            api.ApiKey,
            setup.ProCutServiceId,
            setup.ProCutApiEndpoint,
            setup.ProCutArcFittingEnabled,
            setup.ProCutArcFittingToleranceMm,
            setup.ProCutArcFittingMinSegments,
            setup.ProCutArcFittingMaxSegments,
            setup.ProCutLineJoinerEnabled,
            setup.ProCutLineJoinerPreserveFeedBoundaries,
            setup.ProCutArcJoinerEnabled,
            setup.ProCutArcJoinerMaxCombinedAngleDeg,
            setup.ProCutCornerSmoothEnabled,
            setup.ProCutCornerSmoothAngleThresholdDeg,
            setup.ProCutCornerSmoothSlowdownDistanceMm,
            setup.ProCutCornerSmoothSlowdownFeedrateMmMin,
            setup.ProCutCornerSmoothSmallArcThresholdMm,
            setup.ProCutCornerSmoothIncludeCircularRamps
        }),
        ProcessingMode.ExternalScript => Hash(new
        {
            setup.Mode, setup.ScriptPath, setup.RunnerMode, setup.ArgumentsTemplate,
            ModifiedUtc = File.GetLastWriteTimeUtc(setup.ScriptPath)
        }),
        _ => Hash(setup.Mode)
    };

    private static string Hash<T>(T configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration)));
}
