using Backup.Utility.Internal;

namespace Backup.Utility.Tests;

public sealed class BackupCreatorTests
{
    private static readonly DateTime FixedTime = new(2026, 7, 10, 14, 35, 22);

    [Theory]
    [InlineData(0, BackupStatus.SuccessNoChanges)]
    [InlineData(1, BackupStatus.SuccessCopied)]
    [InlineData(3, BackupStatus.SuccessWithWarnings)]
    public async Task AcceptedExitCodesPublishFinalFolder(int code, BackupStatus expected)
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        string executable = Path.Combine(temp.PathValue, "robocopy.exe");
        File.WriteAllText(executable, string.Empty);
        var creator = new BackupCreator(new FakeRunner(code), new FakeDirectoryOperations(), () => FixedTime, () => executable);

        BackupResult result = await creator.CreateAsync(source, destination, "Test_ccyymmdd_hhmmss");

        Assert.Equal(expected, result.Status);
        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(destination, "Test_20260710_143522")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(destination), p => Path.GetFileName(p).Contains("incomplete"));
    }

    [Theory]
    [InlineData(4, BackupStatus.Incomplete)]
    [InlineData(8, BackupStatus.Failed)]
    [InlineData(16, BackupStatus.FatalFailure)]
    public async Task RejectedExitCodesDoNotPublish(int code, BackupStatus expected)
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        string executable = Path.Combine(temp.PathValue, "robocopy.exe");
        File.WriteAllText(executable, string.Empty);
        var creator = new BackupCreator(new FakeRunner(code), new FakeDirectoryOperations(), () => FixedTime, () => executable);

        BackupResult result = await creator.CreateAsync(source, destination, "Test_ccyymmdd_hhmmss");

        Assert.Equal(expected, result.Status);
        Assert.False(Directory.Exists(Path.Combine(destination, "Test_20260710_143522")));
        Assert.True(result.CleanupAttempted);
        Assert.True(result.CleanupSucceeded);
    }

    [Fact]
    public async Task ExistingFinalFolderIsCollision()
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        Directory.CreateDirectory(Path.Combine(destination, "Test_20260710_143522"));
        var creator = new BackupCreator(new FakeRunner(), new FakeDirectoryOperations(), () => FixedTime, () => "unused");

        BackupResult result = await creator.CreateAsync(source, destination, "Test_ccyymmdd_hhmmss");
        Assert.Equal(BackupStatus.DestinationCollision, result.Status);
    }

    [Fact]
    public async Task CancellationCleansStaging()
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        string executable = Path.Combine(temp.PathValue, "robocopy.exe");
        File.WriteAllText(executable, string.Empty);
        var creator = new BackupCreator(new FakeRunner(cancel: true), new FakeDirectoryOperations(), () => FixedTime, () => executable);

        BackupResult result = await creator.CreateAsync(source, destination, "Test_ccyymmdd_hhmmss");
        Assert.Equal(BackupStatus.Canceled, result.Status);
        Assert.Empty(Directory.EnumerateDirectories(destination));
    }

    [Fact]
    public async Task CleanupFailureLeavesClearlyIncompleteFolder()
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        string executable = Path.Combine(temp.PathValue, "robocopy.exe");
        File.WriteAllText(executable, string.Empty);
        var operations = new FakeDirectoryOperations { FailDeletes = true };
        var creator = new BackupCreator(new FakeRunner(8), operations, () => FixedTime, () => executable);

        BackupResult result = await creator.CreateAsync(source, destination, "Test_ccyymmdd_hhmmss");
        Assert.Equal(BackupStatus.CleanupFailed, result.Status);
        Assert.NotNull(result.IncompleteFolderName);
        Assert.Contains("incomplete", result.IncompleteFolderName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidAndOverlappingPathsAreRejected()
    {
        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string insideSource = Path.Combine(source, "backups");
        Directory.CreateDirectory(insideSource);
        var creator = new BackupCreator();

        Assert.Equal(BackupStatus.InvalidRequest, (await creator.CreateAsync("relative", temp.PathValue, "Test_ccyymmdd_hhmmss")).Status);
        Assert.Equal(BackupStatus.SourceUnavailable, (await creator.CreateAsync(Path.Combine(temp.PathValue, "missing"), temp.PathValue, "Test_ccyymmdd_hhmmss")).Status);
        Assert.Equal(BackupStatus.InvalidRequest, (await creator.CreateAsync(source, source, "Test_ccyymmdd_hhmmss")).Status);
        Assert.Equal(BackupStatus.InvalidRequest, (await creator.CreateAsync(source, insideSource, "Test_ccyymmdd_hhmmss")).Status);
    }
}
