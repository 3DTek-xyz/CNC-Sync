using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Networking;

namespace CNCSync.Tests;

public sealed class SshHostFingerprintTests
{
    internal const string Fingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void NormalizeAcceptsPaddedAndUnpaddedOpenSshFingerprints()
    {
        Assert.Equal(Fingerprint, SshHostFingerprint.Normalize(Fingerprint));
        Assert.Equal(Fingerprint, SshHostFingerprint.Normalize($"  {Fingerprint}=  "));
    }

    [Theory]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:AAAA AAA AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void NormalizeRejectsMalformedOrNonCanonicalFingerprints(string fingerprint) =>
        Assert.Null(SshHostFingerprint.Normalize(fingerprint));

    [Theory]
    [InlineData("")]
    [InlineData("old or invalid fingerprint")]
    [InlineData(Fingerprint)]
    public void FingerprintIsIgnoredUntilVerificationIsEnabled(string fingerprint)
    {
        var destination = new DestinationSettings { SshHostKeyFingerprint = fingerprint };

        Assert.False(destination.RequireSshHostKeyFingerprint);
        Assert.Null(SshConnectionFactory.GetRequiredFingerprint(destination));
    }

    [Fact]
    public void SavedDestinationWithoutTheOptInKeepsVerificationOff()
    {
        var destination = JsonSerializer.Deserialize<DestinationSettings>(
            $$"""{"SshHostKeyFingerprint":"{{Fingerprint}}"}""");

        Assert.NotNull(destination);
        Assert.Equal(Fingerprint, destination.SshHostKeyFingerprint);
        Assert.False(destination.RequireSshHostKeyFingerprint);
    }

    [Fact]
    public async Task TestConnectionUsesNormalAuthenticationWhenFingerprintCheckingIsOff()
    {
        var destination = new DestinationSettings { Host = "localhost", Username = "test", Password = "test" };
        var result = await SshConnectionFactory.TestConnectionAsync<Renci.SshNet.SshClient>(
            destination, "SFTP", (_, _) => throw new InvalidOperationException("Reached normal connection"), default);

        Assert.False(result.Success);
        Assert.Contains("Reached normal connection", result.Message);
    }

    [Theory]
    [InlineData(DestinationType.Sftp)]
    [InlineData(DestinationType.Scp)]
    public async Task UploadRequiresFingerprintBeforeConnecting(DestinationType type)
    {
        var file = Path.GetTempFileName();
        try
        {
            using var scp = new ScpService();
            var destination = new DestinationSettings
            {
                Type = type, Host = "127.0.0.1", Port = 1, Username = "test", Password = "test",
                RequireSshHostKeyFingerprint = true
            };
            var result = type == DestinationType.Scp
                ? await scp.UploadFileSystemItemAsync(file, destination, "/jobs")
                : await new SftpService().UploadFileSystemItemAsync(file, destination, "/jobs");
            Assert.False(result.Success);
            Assert.Contains("SSH server fingerprint is required", result.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
