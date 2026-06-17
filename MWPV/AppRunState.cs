using MWPV.Services.AppLifecycle;

namespace MWPV
{
    internal static class AppRunState
    {
        internal static bool DbOpenedThisRun;   // set to true after valid login
        internal static bool EndLogged;         // prevents duplicate SESSION_END
        internal static string? MigrationFlag;  // optional startup value, retained for future use only
        internal static AppStartupContext StartupContext { get; set; } = AppStartupContext.Normal;
    }
}
