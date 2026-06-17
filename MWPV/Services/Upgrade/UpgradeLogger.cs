using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class UpgradeLogger
    {
        private readonly string? _logPath;

        public UpgradeLogger(string? logPath = null)
        {
            _logPath = logPath;
        }

        public void LogPhase(string phase, string message)
        {
            WriteLine($"phase={phase} message={message}");
            System.Diagnostics.Debug.WriteLine($"[UPGRADE] {phase}: {message}");
        }

        public void LogResult(AppExitCode code, string message)
        {
            WriteLine($"result={(int)code} code={code} message={message}");
            System.Diagnostics.Debug.WriteLine($"[UPGRADE] result={(int)code} {code}: {message}");
        }

        private void WriteLine(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var directory = System.IO.Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    System.IO.Directory.CreateDirectory(directory);

                System.IO.File.AppendAllText(
                    _logPath,
                    $"{System.DateTimeOffset.UtcNow:O} {message}{System.Environment.NewLine}");
            }
            catch
            {
                // Upgrade logging is best-effort and must never affect vault recovery.
            }
        }
    }
}
