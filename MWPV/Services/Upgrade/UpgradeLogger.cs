using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class UpgradeLogger
    {
        public void LogPhase(string phase, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[UPGRADE] {phase}: {message}");
        }

        public void LogResult(AppExitCode code, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[UPGRADE] result={(int)code} {code}: {message}");
        }
    }
}
