// Utilities/Helpers/SevenZipHelper.cs
using System;
using System.IO;
using SevenZip;

namespace Utilities.Helpers
{
    public static class SevenZipHelper
    {
        public static void ConfigureLibraryPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
#if X64
            var dllPath = Path.Combine(baseDir, "7z64.dll");
#else
            var dllPath = Path.Combine(baseDir, "7z.dll");
#endif
            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"7-Zip DLL not found at {dllPath}");

            SevenZipBase.SetLibraryPath(dllPath);
        }
    }
}
