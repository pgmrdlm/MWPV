using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class AppUpgradeCoordinator
    {
        public UpgradeResult RunAuthenticatedUpgrade(AppStartupContext startupContext)
        {
            if (startupContext.RunMode != AppRunMode.Upgrade)
                return UpgradeResult.Success("No upgrade requested.");

            return UpgradeResult.Success("Upgrade coordinator skeleton only.");
        }
    }
}
