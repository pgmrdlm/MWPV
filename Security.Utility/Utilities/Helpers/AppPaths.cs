// File: Utilities/Helpers/AppPaths.cs

using System;
using System.IO;

namespace Utilities.Helpers
{
    internal static class AppPaths
    {
        // Rule:
        // - If exe drive == system drive (usually C:), use per-user LocalAppData
        // - Else, use <exe drive>\AppData\Local
        internal static string LocalAppDataRoot()
        {
            string exeBaseDir = AppContext.BaseDirectory;
            string exeDriveRoot = Path.GetPathRoot(exeBaseDir) ?? string.Empty;

            // System drive root (e.g., "C:\")
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string systemDriveRoot = Path.GetPathRoot(systemDir) ?? string.Empty;

            string root;
            if (string.Equals(exeDriveRoot, systemDriveRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Normal installed/debug scenario: per-user
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else
            {
                // Portable scenario: drive-rooted
                root = Path.Combine(exeDriveRoot, "AppData", "Local");
            }

            Directory.CreateDirectory(root);
            return root;
        }
    }
}
