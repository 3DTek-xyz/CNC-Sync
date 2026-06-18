using CNCSync.Infrastructure.Networking;
using FluentFTP.Exceptions;

namespace CNCSync.Tests;

public sealed class FtpUploadRetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesTransientFailuresBeforeSuccess()
    {
        var attempts = 0;
        var retryAttempts = new List<int>();

        await FtpUploadRetryPolicy.ExecuteAsync(
            async (_, _) =>
            {
                await Task.Yield();
                attempts++;
                if (attempts < 3)
                {
                    throw new TimeoutException("temporary network timeout");
                }
            },
            onRetryAsync: (retryAttempt, _, _, _) =>
            {
                retryAttempts.Add(retryAttempt);
                return Task.CompletedTask;
            },
            delayAsync: (_, _) => Task.CompletedTask,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal([1, 2], retryAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterMaximumRetries()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            FtpUploadRetryPolicy.ExecuteAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    attempts++;
                    throw new TimeoutException("network never recovered");
                },
                delayAsync: (_, _) => Task.CompletedTask,
                cancellationToken: CancellationToken.None));

        Assert.Equal(FtpUploadRetryPolicy.MaxRetries + 1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryPermanentFailures()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FtpUploadRetryPolicy.ExecuteAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    attempts++;
                    throw new InvalidOperationException("bad upload configuration");
                },
                delayAsync: (_, _) => Task.CompletedTask,
                cancellationToken: CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryCancellation()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FtpUploadRetryPolicy.ExecuteAsync(
                async (_, cancellationToken) =>
                {
                    await Task.Yield();
                    attempts++;
                    throw new OperationCanceledException(cancellationToken);
                },
                delayAsync: (_, _) => Task.CompletedTask,
                cancellationToken: CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void IsRetryable_TreatsWrappedIoFailuresAsTransient()
    {
        var exception = new FtpException("FTP transport failed.", new IOException("connection reset"));

        Assert.True(FtpUploadRetryPolicy.IsRetryable(exception));
    }

    [Fact]
    public void IsRetryable_DoesNotRetryAuthenticationFailures()
    {
        var exception = new FtpAuthenticationException("530", "Not logged in.");

        Assert.False(FtpUploadRetryPolicy.IsRetryable(exception));
    }

    [Theory]
    [InlineData("421")]
    [InlineData("425")]
    [InlineData("426")]
    [InlineData("550")]
    public void IsRetryable_RetriesTemporaryAndKnownUploadCommandFailures(string completionCode)
    {
        var exception = new FtpCommandException(completionCode, "Temporary FTP failure.");

        Assert.True(FtpUploadRetryPolicy.IsRetryable(exception));
    }
}
