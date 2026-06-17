using System;
using System.IO;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class UpgradeFlagService
    {
        public OperationResult ClearUpgradeFlag(string? flagFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(flagFilePath))
                    return OperationResult.Success("No upgrade flag file was provided.");

                if (File.Exists(flagFilePath))
                    File.Delete(flagFilePath);

                return OperationResult.Success("Upgrade flag cleared.");
            }
            catch (Exception ex)
            {
                return OperationResult.Failure(
                    AppExitCode.UnknownFatalError,
                    "Upgrade flag clear failed.",
                    ex);
            }
        }

        public OperationResult ClearUpgradeFlagPlaceholder() =>
            OperationResult.Success("Upgrade flag clearing is not implemented yet.");
    }
}
