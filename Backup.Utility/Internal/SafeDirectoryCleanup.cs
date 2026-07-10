namespace Backup.Utility.Internal;

internal interface IDirectoryOperations
{
    void DeleteTree(string path);
}

internal sealed class DirectoryOperations : IDirectoryOperations
{
    public void DeleteTree(string path) => Directory.Delete(path, recursive: true);
}

internal static class SafeDirectoryCleanup
{
    internal static bool TryDeleteCreatedStaging(string parent, string stagingPath, IDirectoryOperations operations)
    {
        try
        {
            if (!BackupPathValidator.IsImmediateChild(parent, stagingPath))
                return false;
            if (Directory.Exists(stagingPath))
                operations.DeleteTree(stagingPath);
            return !Directory.Exists(stagingPath);
        }
        catch
        {
            return false;
        }
    }
}
