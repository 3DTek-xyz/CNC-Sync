namespace CNCSync.Core.Configuration;

public static class SshHostFingerprint
{
    private const string Prefix = "SHA256:";

    public static string? Normalize(string? fingerprint)
    {
        var value = fingerprint?.Trim();
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digest = value[Prefix.Length..];
        Span<byte> bytes = stackalloc byte[32];
        var padded = digest.Length == 43 ? digest + "=" : digest;
        if (padded.Length != 44 || !Convert.TryFromBase64String(padded, bytes, out var written) || written != bytes.Length)
        {
            return null;
        }

        var canonical = Convert.ToBase64String(bytes);
        return padded == canonical ? Prefix + canonical[..^1] : null;
    }
}
