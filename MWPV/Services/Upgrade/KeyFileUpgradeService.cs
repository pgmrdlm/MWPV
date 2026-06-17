using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class KeyFileUpgradeService
    {
        public UpgradeStepResult RewritePlaceholder() =>
            UpgradeStepResult.Failure(
                "RewriteKeyFile",
                UpgradeFailureCategory.KeyFileRewrite,
                AppExitCode.UpgradeKeyFileRewriteFailed,
                "Key-file rewrite is not implemented yet.");

        public UpgradeStepResult ValidatePlaceholder() =>
            UpgradeStepResult.Failure(
                "ValidateKeyFile",
                UpgradeFailureCategory.KeyFileValidation,
                AppExitCode.UpgradeKeyFileValidationFailed,
                "Key-file validation is not implemented yet.");
    }
}
