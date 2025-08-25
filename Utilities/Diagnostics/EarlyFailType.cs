// Utilities/Diagnostics/EarlyFailType.cs
using System;

namespace Utilities.Diagnostics
{
    public enum EarlyFailType
    {
        Unknown = 0,

        // Auth / key setup (10–19)
        InvalidPassword = 10,
        InvalidKeyFile = 11,
        InvalidPasswordOrKeyFile = 12,
        KeyfileMissingOrCorrupt = 13,
        KeyFileVerifyError = 14,

        // Key archive / 7z (20–29)
        KeyArchiveMissing = 20,
        KeyArchiveOpenError = 21,
        ArchiveOpenError = KeyArchiveOpenError,   // compat alias (same numeric value)
        SevenZipNotFound = 22,
        SevenZipExtractError = 23,
        KeyArchiveWriteError = 24,

        // Crypto / storage init (30–39)
        SqlCipherInitError = 30,
        SecureStoreInitFailure = 31,
        SqliteOpenFailed = 32,

        // Policy / other (40–49)
        IllegalLoginAttempt = 40,
        ConfigMissing = 41,

        // Internal / unexpected (90–99)
        UnexpectedException = 99
    }

    public static class EarlyFailTypeExtensions
    {
        public static string Canonical(this EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPassword => "invalid-password",
            EarlyFailType.InvalidKeyFile => "invalid-keyfile",
            EarlyFailType.InvalidPasswordOrKeyFile => "invalid-password-or-keyfile",
            EarlyFailType.KeyfileMissingOrCorrupt => "keyfile-missing-or-corrupt",
            EarlyFailType.KeyFileVerifyError => "keyfile-verify-error",

            EarlyFailType.KeyArchiveMissing => "key-archive-missing",
            EarlyFailType.KeyArchiveOpenError => "key-archive-open-error",   // covers alias too
            EarlyFailType.SevenZipNotFound => "sevenzip-not-found",
            EarlyFailType.SevenZipExtractError => "sevenzip-extract-error",
            EarlyFailType.KeyArchiveWriteError => "key-archive-write-error",

            EarlyFailType.SqlCipherInitError => "sqlcipher-init-error",
            EarlyFailType.SecureStoreInitFailure => "secure-store-init-failure",
            EarlyFailType.SqliteOpenFailed => "sqlite-open-failed",

            EarlyFailType.IllegalLoginAttempt => "illegal-login-attempt",
            EarlyFailType.ConfigMissing => "config-missing",

            EarlyFailType.UnexpectedException => "unexpected-exception",
            _ => "unknown"
        };

        public static string DefaultMessage(this EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPassword => "The password was invalid.",
            EarlyFailType.InvalidKeyFile => "The key file was invalid.",
            EarlyFailType.InvalidPasswordOrKeyFile => "Password or key file was invalid.",
            EarlyFailType.KeyfileMissingOrCorrupt => "The key file is missing or appears corrupt.",
            EarlyFailType.KeyFileVerifyError => "Key file verification failed.",

            EarlyFailType.KeyArchiveMissing => "Required key archive was not found.",
            EarlyFailType.KeyArchiveOpenError => "Failed to open the key archive.", // covers alias
            EarlyFailType.SevenZipNotFound => "7-Zip library not found or could not be loaded.",
            EarlyFailType.SevenZipExtractError => "7-Zip failed to extract required contents.",
            EarlyFailType.KeyArchiveWriteError => "Failed to write or rebuild the key archive.",

            EarlyFailType.SqlCipherInitError => "Failed to initialize SQLCipher.",
            EarlyFailType.SecureStoreInitFailure => "Failed to initialize the secure store.",
            EarlyFailType.SqliteOpenFailed => "SQLite database could not be opened.",

            EarlyFailType.IllegalLoginAttempt => "An unauthorized login attempt was detected.",
            EarlyFailType.ConfigMissing => "A required configuration resource is missing.",

            EarlyFailType.UnexpectedException => "An unexpected exception occurred during early startup.",
            _ => "An unknown early failure occurred."
        };
    }
}
