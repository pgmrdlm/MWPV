using System.Globalization;

namespace Backup.Utility.Internal;

internal sealed record ParsedBackupFolder(string Name, string FullPath, string Prefix, DateTime Timestamp);

internal static class BackupFolderNameParser
{
    internal static bool TryParse(string name, string fullPath, out ParsedBackupFolder? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".", StringComparison.Ordinal) ||
            name.Contains(".incomplete-", StringComparison.OrdinalIgnoreCase))
            return false;

        const int suffixLength = 15; // yyyyMMdd_HHmmss
        if (name.Length < suffixLength + 2)
            return false;

        int separator = name.Length - suffixLength - 1;
        if (separator <= 0 || name[separator] != '_')
            return false;

        string prefix = name[..separator];
        string timestampText = name[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(prefix) ||
            !DateTime.TryParseExact(timestampText, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime timestamp))
            return false;

        parsed = new ParsedBackupFolder(name, fullPath, prefix, timestamp);
        return true;
    }
}
