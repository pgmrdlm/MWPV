using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class DbUpgradeExecutor
    {
        public UpgradeStepResult BuildPlanPlaceholder() =>
            UpgradeStepResult.Failure(
                "BuildPlan",
                UpgradeFailureCategory.Plan,
                AppExitCode.UpgradePlanInvalid,
                "Database upgrade planning is not implemented yet.");

        public UpgradeStepResult ExecutePlaceholder() =>
            UpgradeStepResult.Failure(
                "ExecuteSqlUpgrade",
                UpgradeFailureCategory.SqlExecution,
                AppExitCode.UpgradeSqlExecutionFailed,
                "SQL upgrade execution is not implemented yet.");
    }
}
