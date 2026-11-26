using CNCSync.Infrastructure.Logging;

namespace CNCSync.Tests;

public sealed class ActivityFileLogTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("pcs-activity-test-");

    [Fact]
    public void RotatesBeforeExceedingLimitAndKeepsOnePreviousFile()
    {
        var path = Path.Combine(_directory.FullName, "activity.log");
        var log = new ActivityFileLog(path, maxBytes: 12);

        Assert.True(log.TryAppend("first-line\n"));
        Assert.True(log.TryAppend("second\n"));
        Assert.Equal("first-line\n", File.ReadAllText(path + ".1"));
        Assert.True(log.TryAppend("third\n"));

        Assert.Equal("second\n", File.ReadAllText(path + ".1"));
        Assert.Equal("third\n", File.ReadAllText(path));
        Assert.Equal(2, _directory.GetFiles().Length);
    }

    [Fact]
    public void BoundsOversizedEntriesWithoutSplittingUtf8Characters()
    {
        var path = Path.Combine(_directory.FullName, "activity.log");
        var log = new ActivityFileLog(path, maxBytes: 12);

        Assert.True(log.TryAppend("🖨️🖨️🖨️"));

        Assert.InRange(new FileInfo(path).Length, 1, 12);
        Assert.DoesNotContain('\uFFFD', File.ReadAllText(path));
        Assert.EndsWith(Environment.NewLine, File.ReadAllText(path));
    }

    [Fact]
    public void ReportsAnUnwritablePathWithoutThrowing()
    {
        var path = Path.Combine(_directory.FullName, "activity.log");
        Directory.CreateDirectory(path);
        var log = new ActivityFileLog(path);

        Assert.False(log.TryAppend("entry\n"));
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
