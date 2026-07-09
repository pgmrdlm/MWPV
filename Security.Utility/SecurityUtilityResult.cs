namespace Security.Utility;

/// <summary>
/// Caller-facing technical result for a Security.Utility operation.
/// </summary>
/// <remarks>
/// Security.Utility reports only what happened and how serious it believes the outcome is.
/// The <see cref="Code"/> property identifies the technical outcome. The <see cref="Kind"/>
/// property identifies the technical seriousness classification.
///
/// Security.Utility does not decide UI behavior, popup behavior, retry behavior, abort
/// behavior, recovery behavior, logging policy, application exit code mapping, or
/// user-facing messages.
///
/// Results must not include user-facing message text, raw exception text, stack traces,
/// SQL, secrets, keys, passwords, connection strings, protected payloads, raw encrypted or
/// decrypted payload values, sensitive paths, caller actions, recovery actions, or
/// application exit codes.
/// </remarks>
public sealed record SecurityUtilityResult
{
    /// <summary>
    /// Gets the documented Security.Utility return code describing what happened.
    /// </summary>
    public SecurityUtilityReturnCode Code { get; init; }

    /// <summary>
    /// Gets Security.Utility's technical seriousness classification for the result.
    /// </summary>
    public SecurityUtilityResultKind Kind { get; init; }

    /// <summary>
    /// Gets a value indicating whether the documented return code is
    /// <see cref="SecurityUtilityReturnCode.Success"/>.
    /// </summary>
    public bool Succeeded => Code == SecurityUtilityReturnCode.Success;

    /// <summary>
    /// Gets a value indicating whether Security.Utility classified the result as abend-level.
    /// The caller decides what behavior, if any, follows from that classification.
    /// </summary>
    public bool IsAbend => Kind == SecurityUtilityResultKind.Abend;

    /// <summary>
    /// Gets an optional opaque caller-safe reference code. This value must never contain
    /// exception text, stack traces, SQL, secrets, keys, passwords, connection strings,
    /// protected payloads, sensitive paths, caller actions, or user-facing messages.
    /// </summary>
    public string? SafeReferenceCode { get; init; }
}
