using System;

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Categorizes "early" failures that occur before encrypted DB logging is available.
    /// Keep names stable — they’re used in filenames and ingestion.
    /// </summary>
    public enum EarlyFailType
    {
        Unknown = 0,

        // Auth / key setup
        InvalidPassword = 10,
        InvalidKeyFile = 11,
        InvalidPasswordOrKeyFile = 12,      // referenced by callers
        KeyfileMissingOrCorrupt = 13,       // referenced by callers (note lowercase 'f' per caller)
        KeyFileVerifyError = 14,            // referenced by callers

        // Key archive / 7z
        KeyArchiveMissing = 20,
        KeyArchiveOpenError = 21,
        SevenZipNotFound = 22,
        SevenZipExtractError = 23,

        // Crypto / storage init
        SqlCipherInitError = 30,
        SecureStoreInitFailure = 31,
        SqliteOpenFailed = 32,

        // Policy / other
        IllegalLoginAttempt = 40,
        ConfigMissing = 41,
    }

    public static class EarlyFailTypeExtensions
    {
        /// <summary>Canonical, filesystem-safe identifier used in .elog headers & filenames.</summary>
        public static string Canonical(this EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPassword => "invalid-password",
            EarlyFailType.InvalidKeyFile => "invalid-keyfile",
            EarlyFailType.InvalidPasswordOrKeyFile => "invalid-password-or-keyfile",
            EarlyFailType.KeyfileMissingOrCorrupt => "keyfile-missing-or-corrupt",
            EarlyFailType.KeyFileVerifyError => "keyfile-verify-error",

            EarlyFailType.KeyArchiveMissing => "key-archive-missing",
            EarlyFailType.KeyArchiveOpenError => "key-archive-open-error",
            EarlyFailType.SevenZipNotFound => "sevenzip-not-found",
            EarlyFailType.SevenZipExtractError => "sevenzip-extract-error",

            EarlyFailType.SqlCipherInitError => "sqlcipher-init-error",
            EarlyFailType.SecureStoreInitFailure => "secure-store-init-failure",
            EarlyFailType.SqliteOpenFailed => "sqlite-open-failed",

            EarlyFailType.IllegalLoginAttempt => "illegal-login-attempt",
            EarlyFailType.ConfigMissing => "config-missing",

            EarlyFailType.Unknown or _ => "unknown"
        };

        /// <summary>Human-readable default message for when a caller doesn’t supply one.</summary>
        public static string DefaultMessage(this EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPassword => "The password was invalid.",
            EarlyFailType.InvalidKeyFile => "The key file was invalid.",
            EarlyFailType.InvalidPasswordOrKeyFile => "Password or key file was invalid.",
            EarlyFailType.KeyfileMissingOrCorrupt => "The key file is missing or appears corrupt.",
            EarlyFailType.KeyFileVerifyError => "Key file verification failed.",

            EarlyFailType.KeyArchiveMissing => "Required key archive was not found.",
            EarlyFailType.KeyArchiveOpenError => "Failed to open the key archive.",
            EarlyFailType.SevenZipNotFound => "7-Zip library not found or could not be loaded.",
            EarlyFailType.SevenZipExtractError => "7-Zip failed to extract required contents.",

            EarlyFailType.SqlCipherInitError => "Failed to initialize SQLCipher.",
            EarlyFailType.SecureStoreInitFailure => "Failed to initialize secure store.",
            EarlyFailType.SqliteOpenFailed => "SQLite database could not be opened.",

            EarlyFailType.IllegalLoginAttempt => "An unauthorized login attempt was detected.",
            EarlyFailType.ConfigMissing => "A required configuration resource is missing.",

            EarlyFailType.Unknown or _ => "An unknown early failure occurred."
        };
    }
}
