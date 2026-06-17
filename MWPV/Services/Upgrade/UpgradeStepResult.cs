using System;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeStepResult
    {
        public bool Succeeded { get; init; }
        public string StepName { get; init; } = string.Empty;
        public UpgradeFailureCategory FailureCategory { get; init; } = UpgradeFailureCategory.None;
        public AppExitCode DetailCode { get; init; } = AppExitCode.Success;
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public static UpgradeStepResult Success(string stepName, string message = "") =>
            new() { Succeeded = true, StepName = stepName, Message = message };

        public static UpgradeStepResult Failure(
            string stepName,
            UpgradeFailureCategory category,
            AppExitCode detailCode,
            string message,
            Exception? exception = null) =>
            new()
            {
                Succeeded = false,
                StepName = stepName,
                FailureCategory = category,
                DetailCode = detailCode,
                Message = message,
                Exception = exception
            };
    }
}
