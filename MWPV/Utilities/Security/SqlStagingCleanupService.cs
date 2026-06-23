using System;
using System.IO;
using Security.Utility.Wiping;
using Utilities.Helpers;

namespace Utilities.Security
{
    internal static class SqlStagingCleanupService
    {
        public static void SecurelyScrubDefaultStagingFolder()
        {
            SecurelyScrubStagingFolder(DatabaseHelper.GetSqlFolderPath());
        }

        public static bool TrySecurelyScrubDefaultStagingFolder(out Exception? exception)
        {
            try
            {
                SecurelyScrubDefaultStagingFolder();
                exception = null;
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        private static void SecurelyScrubStagingFolder(string sqlFolder)
        {
            if (!Directory.Exists(sqlFolder))
                return;

            SensitiveDataCleaner.SecureDeleteAllFiles(sqlFolder, overwritePasses: 3);

            try
            {
                SensitiveDataCleaner.SecureDeleteDirectory(
                    sqlFolder,
                    overwritePasses: 1,
                    shredNames: true,
                    finalZeroPass: false,
                    removeDirectories: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
