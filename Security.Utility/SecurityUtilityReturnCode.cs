namespace Security.Utility;

/// <summary>
/// Documents the technical outcome of a Security.Utility operation.
/// Return codes say what happened; <see cref="SecurityUtilityResultKind"/> says how
/// serious Security.Utility believes the outcome is. Security.Utility must not use
/// these codes to choose caller behavior.
/// </summary>
/// <remarks>
/// Callers own UI behavior, popup behavior, retry behavior, abort behavior, recovery
/// behavior, logging policy, application exit code mapping, and user-facing messages.
/// Return codes may wrap Microsoft, .NET, or system exceptions, but callers should use
/// the documented Security.Utility code instead of interpreting raw exceptions.
/// </remarks>
public enum SecurityUtilityReturnCode
{
    /// <summary>
    /// The security operation completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The caller supplied invalid input for the requested security operation.
    /// </summary>
    InvalidInput = 1000,

    /// <summary>
    /// A requested secure-store key was not present.
    /// </summary>
    SecureStoreKeyMissing = 1010,

    /// <summary>
    /// The secure store was unavailable for the requested operation.
    /// </summary>
    SecureStoreUnavailable = 1011,

    /// <summary>
    /// A required cryptographic key was missing.
    /// </summary>
    CryptoKeyMissing = 1020,

    /// <summary>
    /// A cryptographic key was present but invalid for the requested operation.
    /// </summary>
    CryptoKeyInvalid = 1021,

    /// <summary>
    /// Protected data was present but malformed before decryption could proceed.
    /// </summary>
    ProtectedDataMalformed = 1030,

    /// <summary>
    /// Protected data could not be decrypted or authenticated.
    /// </summary>
    ProtectedDataDecryptFailed = 1031,

    /// <summary>
    /// A keyset payload was invalid, malformed, or unsupported.
    /// </summary>
    KeysetInvalid = 1040,

    /// <summary>
    /// A required keyset section, payload, or value was missing.
    /// </summary>
    RequiredPayloadMissing = 1041,

    /// <summary>
    /// A password did not satisfy the technical password policy.
    /// </summary>
    PasswordPolicyFailed = 1050,

    /// <summary>
    /// Secure delete or sensitive wipe did not complete successfully.
    /// </summary>
    SecureDeleteFailed = 1060,

    /// <summary>
    /// An unexpected security failure occurred and no more specific code applies.
    /// </summary>
    UnknownSecurityFailure = 1099
}
