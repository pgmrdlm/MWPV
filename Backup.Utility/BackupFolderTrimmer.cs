using Backup.Utility.Internal;

namespace Backup.Utility;

public sealed class BackupFolderTrimmer
{
    private readonly IDirectoryOperations _directories;

    public BackupFolderTrimmer() : this(new DirectoryOperations()) { }

    internal BackupFolderTrimmer(IDirectoryOperations directories) => _directories = directories;

    public Task<TrimResult> TrimAsync(
        string highLevelFolder,
        int retainCount,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Trim(highLevelFolder, retainCount, cancellationToken), CancellationToken.None);
    }

    private TrimResult Trim(string highLevelFolder, int retainCount, CancellationToken cancellationToken)
    {
        var eligible = new List<ParsedBackupFolder>();
        var skipped = new List<string>();
        var retained = new List<string>();
        var deleted = new List<string>();
        var failed = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (retainCount < 1 || string.IsNullOrWhiteSpace(highLevelFolder) || !Path.IsPathRooted(highLevelFolder))
                return Build(TrimStatus.InvalidRequest, "A rooted folder and retention count of at least one are required.");

            string parent;
            try { parent = BackupPathValidator.Normalize(highLevelFolder); }
            catch { return Build(TrimStatus.InvalidRequest, "The supplied folder path is invalid."); }

            if (!Directory.Exists(parent))
                return Build(TrimStatus.FolderUnavailable, "The backup folder is unavailable.");

            foreach (string child in Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(child);

                try
                {
                    if (!BackupPathValidator.IsImmediateChild(parent, child) ||
                        (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 ||
                        !BackupFolderNameParser.TryParse(name, child, out ParsedBackupFolder? parsed) || parsed == null)
                    {
                        skipped.Add(name);
                        continue;
                    }

                    eligible.Add(parsed);
                }
                catch
                {
                    skipped.Add(name);
                }
            }

            if (eligible.Select(x => x.Prefix).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                return Build(TrimStatus.AmbiguousBackupFamily, "More than one backup family was found; nothing was deleted.");

            var ordered = eligible
                .OrderByDescending(x => x.Timestamp)
                .ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            retained.AddRange(ordered.Take(retainCount).Select(x => x.Name));
            foreach (var target in ordered.Skip(retainCount))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!BackupPathValidator.IsImmediateChild(parent, target.FullPath) ||
                        (File.GetAttributes(target.FullPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped.Add(target.Name);
                        continue;
                    }

                    _directories.DeleteTree(target.FullPath);
                    if (Directory.Exists(target.FullPath))
                        failed.Add(target.Name);
                    else
                        deleted.Add(target.Name);
                }
                catch
                {
                    failed.Add(target.Name);
                }
            }

            TrimStatus status = failed.Count > 0
                ? TrimStatus.PartialFailure
                : skipped.Count > 0 ? TrimStatus.SuccessWithSkippedFolders : TrimStatus.Success;
            return Build(status, failed.Count > 0 ? "Some eligible backup folders could not be deleted." : "Backup retention trimming completed.");
        }
        catch (OperationCanceledException)
        {
            return Build(TrimStatus.Canceled, "Backup retention trimming was canceled.");
        }
        catch
        {
            return Build(TrimStatus.Failed, "Backup retention trimming failed.");
        }

        TrimResult Build(TrimStatus status, string message) => new()
        {
            Status = status,
            SafeMessage = message,
            RequestedRetentionCount = retainCount,
            EligibleFolderNames = eligible.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            RetainedFolderNames = retained.ToArray(),
            DeletedFolderNames = deleted.ToArray(),
            SkippedFolderNames = skipped.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FailedDeletionFolderNames = failed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
