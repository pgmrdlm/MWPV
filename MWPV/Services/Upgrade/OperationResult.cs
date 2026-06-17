using System;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed record OperationResult
    {
        public bool Succeeded { get; init; }
        public AppExitCode Code { get; init; } = AppExitCode.Success;
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public static OperationResult Success(string message = "") =>
            new() { Succeeded = true, Message = message };

        public static OperationResult Failure(AppExitCode code, string message, Exception? exception = null) =>
            new() { Succeeded = false, Code = code, Message = message, Exception = exception };
    }

    public sealed record OperationResult<T>
    {
        public bool Succeeded { get; init; }
        public AppExitCode Code { get; init; } = AppExitCode.Success;
        public string Message { get; init; } = string.Empty;
        public T? Value { get; init; }
        public Exception? Exception { get; init; }

        public static OperationResult<T> Success(T value, string message = "") =>
            new() { Succeeded = true, Value = value, Message = message };

        public static OperationResult<T> Failure(AppExitCode code, string message, Exception? exception = null) =>
            new() { Succeeded = false, Code = code, Message = message, Exception = exception };
    }
}
