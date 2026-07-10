using System.Globalization;

namespace Backup.Utility.Internal;

internal static class BackupNameResolver
{
    internal const string TimestampToken = "ccyymmdd_hhmmss";

    internal static bool TryResolve(string? template, DateTime localTime, out string name, out string message)
    {
        name = string.Empty;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(template))
        {
            message = "A backup name template is required.";
            return false;
        }

        if (!string.Equals(template, Path.GetFileName(template), StringComparison.Ordinal) ||
            template is "." or ".." || template.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            message = "The backup name template is invalid.";
            return false;
        }

        int first = template.IndexOf(TimestampToken, StringComparison.Ordinal);
        int last = template.LastIndexOf(TimestampToken, StringComparison.Ordinal);
        if (first < 0 || first != last || first + TimestampToken.Length != template.Length)
        {
            message = "The template must contain exactly one case-sensitive timestamp token at the end.";
            return false;
        }

        string prefix = template[..first];
        if (prefix.Length < 2 || prefix[^1] != '_' || string.IsNullOrWhiteSpace(prefix[..^1]))
        {
            message = "The template must have a nonempty prefix followed by an underscore.";
            return false;
        }

        name = prefix + localTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return true;
    }
}
