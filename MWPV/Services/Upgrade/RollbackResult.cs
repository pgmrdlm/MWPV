using System;

namespace MWPV.Services.Upgrade
{
    public sealed record RollbackResult
    {
        public RollbackResultStatus Status { get; init; } = RollbackResultStatus.NotRequired;
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public static RollbackResult NotRequired(string message = "") =>
            new() { Status = RollbackResultStatus.NotRequired, Message = message };

        public static RollbackResult Succeeded(string message = "") =>
            new() { Status = RollbackResultStatus.Succeeded, Message = message };

        public static RollbackResult Failed(string message, Exception? exception = null) =>
            new() { Status = RollbackResultStatus.Failed, Message = message, Exception = exception };
    }
}
