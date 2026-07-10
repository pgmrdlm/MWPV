namespace Backup.Utility.Tests;

public sealed class RobocopyIntegrationTests
{
    [Fact]
    public async Task RealRobocopyCopiesNestedFilesAndEmptyDirectories()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();
        string source = temp.Create("source");
        string destination = temp.Create("destination");
        string nested = Path.Combine(source, "nested");
        string empty = Path.Combine(source, "empty");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(source, "root.txt"), "root");
        File.WriteAllText(Path.Combine(nested, "child.txt"), "child");

        BackupResult result = await new BackupCreator().CreateAsync(
            source,
            destination,
            "Integration_ccyymmdd_hhmmss");

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.True(File.Exists(Path.Combine(result.ResolvedFullPath, "root.txt")));
        Assert.True(File.Exists(Path.Combine(result.ResolvedFullPath, "nested", "child.txt")));
        Assert.True(Directory.Exists(Path.Combine(result.ResolvedFullPath, "empty")));
    }
}
