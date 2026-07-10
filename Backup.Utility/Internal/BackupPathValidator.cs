namespace Backup.Utility.Internal;

internal static class BackupPathValidator
{
    internal static bool TryValidateBackupPaths(
        string? source,
        string? destinationParent,
        out string fullSource,
        out string fullDestination,
        out BackupStatus status,
        out string message)
    {
        fullSource = string.Empty;
        fullDestination = string.Empty;
        status = BackupStatus.InvalidRequest;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destinationParent) ||
            !Path.IsPathRooted(source) || !Path.IsPathRooted(destinationParent))
        {
            message = "Rooted source and destination paths are required.";
            return false;
        }

        try
        {
            fullSource = Normalize(source);
            fullDestination = Normalize(destinationParent);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            message = "A supplied path is invalid.";
            return false;
        }

        if (!Directory.Exists(fullSource))
        {
            status = BackupStatus.SourceUnavailable;
            message = "The source folder is unavailable.";
            return false;
        }

        if (!Directory.Exists(fullDestination))
        {
            status = BackupStatus.DestinationUnavailable;
            message = "The destination parent folder is unavailable.";
            return false;
        }

        if (PathsOverlap(fullSource, fullDestination))
        {
            message = "Source and destination folders must not overlap.";
            return false;
        }

        return true;
    }

    internal static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool IsImmediateChild(string parent, string child)
    {
        string normalizedParent = Normalize(parent);
        string? actualParent = Path.GetDirectoryName(Normalize(child));
        return actualParent != null && string.Equals(normalizedParent, Normalize(actualParent), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsOverlap(string left, string right) =>
        IsSameOrChild(left, right) || IsSameOrChild(right, left);

    private static bool IsSameOrChild(string parent, string candidate)
    {
        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
