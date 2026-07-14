using System;
using System.Security.Cryptography;
using Security.Utility.Crypto.Fields;
using Security.Utility.Crypto.Signatures;
using Security.Utility.Storage;
using Security.Utility.Wiping;
using Utilities.Helpers;

namespace MWPV.Utilities.Security
{
    /// <summary>
    /// Compares a submitted password to an existing password-history fingerprint without
    /// retaining the original password plaintext.
    /// </summary>
    public enum PasswordFingerprintComparisonResult
    {
        Unchanged,
        Changed,
        Unverifiable
    }

    public static class PasswordFingerprintComparer
    {
        // Must remain compatible with the fingerprint comparison previously used by
        // CategoryItemEditorTabs for password-history and password-change logging.
        private const string FingerprintPurpose = SensitiveValueSignature.DefaultPurpose;

        public static PasswordFingerprintComparisonResult Compare(
            string? submittedPassword,
            byte[]? originalFingerprint)
        {
            if (originalFingerprint == null || originalFingerprint.Length == 0)
                return PasswordFingerprintComparisonResult.Unverifiable;

            byte[]? submittedFingerprint = null;
            try
            {
                submittedFingerprint = ComputeFingerprint(submittedPassword);

                return originalFingerprint.Length == submittedFingerprint.Length &&
                       CryptographicOperations.FixedTimeEquals(originalFingerprint, submittedFingerprint)
                    ? PasswordFingerprintComparisonResult.Unchanged
                    : PasswordFingerprintComparisonResult.Changed;
            }
            finally
            {
                SensitiveDataCleaner.Zero(submittedFingerprint);
            }
        }

        private static byte[] ComputeFingerprint(string? passwordPlain)
        {
            var result = SecureEncryptedDataStore.TryGetBytesResult(
                FieldAesCrypto.SedsKey_UserSecretsKey,
                out var keyBytes);

            if (!result.Succeeded)
            {
                ErrorHandler.Warn(
                    "Security.Utility",
                    $"UserSecretsKey read failed during password fingerprint comparison. SecurityUtilityCode={result.Code}; SecurityUtilityKind={result.Kind}.");
                throw new InvalidOperationException("Required security key material is unavailable.");
            }

            try
            {
                return SensitiveValueSignature.Compute(
                    passwordPlain ?? string.Empty,
                    keyBytes,
                    purpose: FingerprintPurpose);
            }
            finally
            {
                SensitiveDataCleaner.Zero(keyBytes);
            }
        }
    }
}
