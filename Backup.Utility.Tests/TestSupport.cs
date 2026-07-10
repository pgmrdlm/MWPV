using Backup.Utility.Internal;

namespace Backup.Utility.Tests;

internal sealed class FakeRunner : IRobocopyRunner
{
    private readonly int _exitCode;
    private readonly bool _cancel;
    private readonly bool _throw;

    internal FakeRunner(int exitCode = 1, bool cancel = false, bool throwOnRun = false)
    {
        _exitCode = exitCode;
        _cancel = cancel;
        _throw = throwOnRun;
    }

    public Task<RobocopyRunResult> RunAsync(string executable, string source, string destination, CancellationToken cancellationToken)
    {
        if (_cancel) throw new OperationCanceledException(cancellationToken);
        if (_throw) throw new InvalidOperationException("test");
        File.WriteAllText(Path.Combine(destination, "copied.txt"), "copied");
        return Task.FromResult(new RobocopyRunResult(_exitCode));
    }
}

internal sealed class FakeDirectoryOperations : IDirectoryOperations
{
    internal bool FailDeletes { get; set; }
    internal HashSet<string> FailNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void DeleteTree(string path)
    {
        if (FailDeletes || FailNames.Contains(Path.GetFileName(path)))
            throw new IOException("test");
        Directory.Delete(path, true);
    }
}

internal sealed class TempDirectory : IDisposable
{
    internal string PathValue { get; } = Path.Combine(Path.GetTempPath(), "BackupUtilityTests", Guid.NewGuid().ToString("N"));

    internal TempDirectory() => Directory.CreateDirectory(PathValue);
    internal string Create(string name) { string path = Path.Combine(PathValue, name); Directory.CreateDirectory(path); return path; }
    public void Dispose() { try { Directory.Delete(PathValue, true); } catch { } }
}
