namespace MWPV.Services.AppLifecycle
{
    internal static class AppLifecycleTransitionService
    {
        internal static void CompleteSuccessfulUpgrade()
        {
            AppRunState.StartupContext = AppRunState.StartupContext with
            {
                RunMode = AppRunMode.Normal
            };

            VaultSessionStateService.Reset();
        }
    }
}
