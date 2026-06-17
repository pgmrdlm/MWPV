using System;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeResult
    {
        public bool Succeeded { get; init; }
        public AppExitCode FinalExitCode { get; init; } = AppExitCode.Success;
        public AppExitCode? DetailCode { get; init; }
        public UpgradeFailureCategory FailureCategory { get; init; } = UpgradeFailureCategory.None;
        public bool BackupSetCreated { get; init; }
        public bool RollbackRequired { get; init; }
        public RollbackResult Rollback { get; init; } = RollbackResult.NotRequired();
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public static UpgradeResult Success(string message = "") =>
            new() { Succeeded = true, Message = message };

        public static UpgradeResult Failure(
            AppExitCode finalExitCode,
            AppExitCode? detailCode,
            UpgradeFailureCategory category,
            string message,
            bool backupSetCreated = false,
            bool rollbackRequired = false,
            RollbackResult? rollback = null,
            Exception? exception = null) =>
            new()
            {
                Succeeded = false,
                FinalExitCode = finalExitCode,
                DetailCode = detailCode,
                FailureCategory = category,
                BackupSetCreated = backupSetCreated,
                RollbackRequired = rollbackRequired,
                Rollback = rollback ?? RollbackResult.NotRequired(),
                Message = message,
                Exception = exception
            };
    }
}
