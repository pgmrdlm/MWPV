namespace Backup.Utility.Tests;

public sealed class BackupFolderTrimmerTests
{
    [Fact]
    public async Task RetainsNewestAndDeletesOlderImmediateChildren()
    {
        using var temp = new TempDirectory();
        string root = temp.Create("backups");
        foreach (string name in new[] { "DB_20260710_100000", "DB_20260709_100000", "DB_20260708_100000" })
            Directory.CreateDirectory(Path.Combine(root, name));
        Directory.CreateDirectory(Path.Combine(root, "DB_20260710_100000", "nested"));

        TrimResult result = await new BackupFolderTrimmer().TrimAsync(root, 2);

        Assert.Equal(TrimStatus.Success, result.Status);
        Assert.Equal(new[] { "DB_20260710_100000", "DB_20260709_100000" }, result.RetainedFolderNames);
        Assert.Equal(new[] { "DB_20260708_100000" }, result.DeletedFolderNames);
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UnsafeRetentionIsRejected(int retainCount)
    {
        using var temp = new TempDirectory();
        TrimResult result = await new BackupFolderTrimmer().TrimAsync(temp.PathValue, retainCount);
        Assert.Equal(TrimStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task PreCanceledRequestReturnsStructuredCanceledResult()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        TrimResult result = await new BackupFolderTrimmer().TrimAsync(temp.PathValue, 1, cancellation.Token);
        Assert.Equal(TrimStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task MalformedAndIncompleteFoldersAreSkipped()
    {
        using var temp = new TempDirectory();
        string root = temp.Create("backups");
        Directory.CreateDirectory(Path.Combine(root, "DB_20260710_100000"));
        Directory.CreateDirectory(Path.Combine(root, "unrelated"));
        Directory.CreateDirectory(Path.Combine(root, ".DB_20260711_100000.incomplete-abc"));

        TrimResult result = await new BackupFolderTrimmer().TrimAsync(root, 1);
        Assert.Equal(TrimStatus.SuccessWithSkippedFolders, result.Status);
        Assert.Equal(2, result.SkippedFolderNames.Count);
        Assert.Empty(result.DeletedFolderNames);
    }

    [Fact]
    public async Task MixedPrefixesAreAmbiguousAndDeleteNothing()
    {
        using var temp = new TempDirectory();
        string root = temp.Create("backups");
        Directory.CreateDirectory(Path.Combine(root, "DB_20260710_100000"));
        Directory.CreateDirectory(Path.Combine(root, "CODE_20260709_100000"));

        TrimResult result = await new BackupFolderTrimmer().TrimAsync(root, 1);
        Assert.Equal(TrimStatus.AmbiguousBackupFamily, result.Status);
        Assert.Empty(result.DeletedFolderNames);
        Assert.Equal(2, Directory.EnumerateDirectories(root).Count());
    }

    [Fact]
    public async Task IndividualDeletionFailureProducesPartialResult()
    {
        using var temp = new TempDirectory();
        string root = temp.Create("backups");
        Directory.CreateDirectory(Path.Combine(root, "DB_20260710_100000"));
        Directory.CreateDirectory(Path.Combine(root, "DB_20260709_100000"));
        var operations = new FakeDirectoryOperations();
        operations.FailNames.Add("DB_20260709_100000");

        TrimResult result = await new BackupFolderTrimmer(operations).TrimAsync(root, 1);
        Assert.Equal(TrimStatus.PartialFailure, result.Status);
        Assert.Equal(new[] { "DB_20260709_100000" }, result.FailedDeletionFolderNames);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task FewerOrExactFoldersDeleteNothing()
    {
        using var temp = new TempDirectory();
        string root = temp.Create("backups");
        Directory.CreateDirectory(Path.Combine(root, "DB_20260710_100000"));
        Directory.CreateDirectory(Path.Combine(root, "DB_20260709_100000"));

        Assert.Empty((await new BackupFolderTrimmer().TrimAsync(root, 3)).DeletedFolderNames);
        Assert.Empty((await new BackupFolderTrimmer().TrimAsync(root, 2)).DeletedFolderNames);
    }
}
