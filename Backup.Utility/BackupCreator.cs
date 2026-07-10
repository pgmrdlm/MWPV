using Backup.Utility.Internal;

namespace Backup.Utility;

public sealed class BackupCreator
{
    private readonly IRobocopyRunner _runner;
    private readonly IDirectoryOperations _directories;
    private readonly Func<DateTime> _localNow;
    private readonly Func<string> _robocopyPath;

    public BackupCreator()
        : this(new RobocopyRunner(), new DirectoryOperations(), () => DateTime.Now, ResolveRobocopyPath)
    {
    }

    internal BackupCreator(
        IRobocopyRunner runner,
        IDirectoryOperations directories,
        Func<DateTime> localNow,
        Func<string> robocopyPath)
    {
        _runner = runner;
        _directories = directories;
        _localNow = localNow;
        _robocopyPath = robocopyPath;
    }

    public async Task<BackupResult> CreateAsync(
        string sourceFolder,
        string destinationParentFolder,
        string backupNameTemplate,
        CancellationToken cancellationToken = default)
    {
        string resolvedName = string.Empty;
        string finalPath = string.Empty;
        string stagingPath = string.Empty;
        bool stagingCreated = false;
        int? exitCode = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!BackupPathValidator.TryValidateBackupPaths(
                    sourceFolder, destinationParentFolder,
                    out string source, out string destination,
                    out BackupStatus invalidStatus, out string invalidMessage))
            {
                return Result(invalidStatus, invalidMessage);
            }

            if (!BackupNameResolver.TryResolve(backupNameTemplate, _localNow(), out resolvedName, out string nameMessage))
                return Result(BackupStatus.InvalidRequest, nameMessage);

            finalPath = Path.Combine(destination, resolvedName);
            if (Directory.Exists(finalPath) || File.Exists(finalPath))
                return Result(BackupStatus.DestinationCollision, "The resolved backup destination already exists.");

            string robocopy = _robocopyPath();
            if (string.IsNullOrWhiteSpace(robocopy) || !File.Exists(robocopy))
                return Result(BackupStatus.RobocopyUnavailable, "Robocopy is unavailable on this Windows installation.");

            stagingPath = Path.Combine(destination, $".{resolvedName}.incomplete-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingPath);
            stagingCreated = true;

            var run = await _runner.RunAsync(robocopy, source, stagingPath, cancellationToken).ConfigureAwait(false);
            exitCode = run.ExitCode;
            var mapped = RobocopyExitCodeMapper.Map(run.ExitCode);

            if (!mapped.CanPublish)
                return CleanupFailureResult(mapped.Status, "The backup did not complete successfully.", mapped);

            if (Directory.Exists(finalPath) || File.Exists(finalPath))
                return CleanupFailureResult(BackupStatus.DestinationCollision, "The resolved backup destination already exists.", mapped);

            Directory.Move(stagingPath, finalPath);
            stagingCreated = false;

            return new BackupResult
            {
                Status = mapped.Status,
                SafeMessage = mapped.Status == BackupStatus.SuccessWithWarnings
                    ? "The backup completed with warnings."
                    : "The backup completed successfully.",
                ResolvedFolderName = resolvedName,
                ResolvedFullPath = finalPath,
                RobocopyExitCode = exitCode,
                FilesCopied = mapped.FilesCopied,
                WarningsOccurred = mapped.WarningsOccurred,
                CleanupSucceeded = true
            };
        }
        catch (OperationCanceledException)
        {
            return CleanupFailureResult(BackupStatus.Canceled, "The backup was canceled.", default);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return CleanupFailureResult(BackupStatus.ProcessLaunchFailed, "Robocopy could not be started.", default);
        }
        catch
        {
            return CleanupFailureResult(BackupStatus.Failed, "The backup operation failed.", default);
        }

        BackupResult CleanupFailureResult(BackupStatus intendedStatus, string message, RobocopyMapping mapped)
        {
            bool attempted = stagingCreated;
            bool cleaned = !stagingCreated || SafeDirectoryCleanup.TryDeleteCreatedStaging(
                Path.GetDirectoryName(stagingPath) ?? string.Empty, stagingPath, _directories);

            return new BackupResult
            {
                Status = cleaned ? intendedStatus : BackupStatus.CleanupFailed,
                SafeMessage = cleaned ? message : "The backup failed and its incomplete folder could not be removed.",
                ResolvedFolderName = resolvedName,
                ResolvedFullPath = finalPath,
                RobocopyExitCode = exitCode,
                FilesCopied = mapped.FilesCopied,
                WarningsOccurred = mapped.WarningsOccurred,
                CleanupAttempted = attempted,
                CleanupSucceeded = cleaned,
                IncompleteFolderName = cleaned || string.IsNullOrEmpty(stagingPath) ? null : Path.GetFileName(stagingPath)
            };
        }

        BackupResult Result(BackupStatus status, string message) => new()
        {
            Status = status,
            SafeMessage = message,
            ResolvedFolderName = resolvedName,
            ResolvedFullPath = finalPath,
            CleanupSucceeded = true
        };
    }

    private static string ResolveRobocopyPath()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(windows, "System32", "robocopy.exe");
    }
}
