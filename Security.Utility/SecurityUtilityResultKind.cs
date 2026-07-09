namespace Security.Utility;

/// <summary>
/// Describes how serious Security.Utility considers a technical security outcome.
/// Security.Utility reports this classification only; callers decide UI behavior,
/// popup behavior, retry behavior, abort behavior, recovery behavior, logging policy,
/// application exit code mapping, and user-facing messages.
/// </summary>
public enum SecurityUtilityResultKind
{
    /// <summary>
    /// The security operation completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The condition is non-fatal by itself. The caller decides how to handle it.
    /// </summary>
    Warning = 10,

    /// <summary>
    /// The security operation failed, but Security.Utility does not decide caller behavior.
    /// </summary>
    Failure = 20,

    /// <summary>
    /// Security.Utility considers the result security-critical or abend-level for the
    /// current security operation. The caller still owns all behavior decisions.
    /// </summary>
    Abend = 30
}
