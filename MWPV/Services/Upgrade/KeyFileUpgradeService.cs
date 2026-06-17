using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using KeyFileLogic;
using Security.Utility;
using Security.Utility.Crypto;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class KeyFileUpgradeService
    {
        private const long KeysetPayloadId = 1;

        public UpgradeStepResult RewriteSqlPayload(
            string keyFilePath,
            char[] keyPassword,
            UpgradeSqlCatalog catalog,
            bool mergeExistingSql = false)
        {
            byte[]? payloadBytes = null;
            byte[]? rewrittenBytes = null;

            try
            {
                if (catalog == null)
                {
                    return UpgradeStepResult.Failure(
                        "RewriteKeyFile",
                        UpgradeFailureCategory.KeyFileRewrite,
                        AppExitCode.UpgradeKeyFileRewriteFailed,
                        "Upgrade SQL catalog is required.");
                }

                var sqlMap = catalog.GetSqlMapForKeyFileRebuild();
                if (sqlMap.Count == 0)
                {
                    return UpgradeStepResult.Failure(
                        "RewriteKeyFile",
                        UpgradeFailureCategory.KeyFileRewrite,
                        AppExitCode.UpgradeKeyFileRewriteFailed,
                        "No installed normal SQL files were loaded for key-file rebuild.");
                }

                var (directory, fileName) = SplitKeyFilePath(keyFilePath);
                payloadBytes = KeyFileStore.ReadPayloadBytes(directory, fileName, keyPassword, KeysetPayloadId);
                var json = Encoding.UTF8.GetString(payloadBytes);
                var keyset = KeysetJsonV2.Deserialize(json);

                var updatedSql = mergeExistingSql
                    ? new Dictionary<string, string>(keyset.sql, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var pair in sqlMap)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                        updatedSql[pair.Key] = pair.Value ?? string.Empty;
                }

                keyset.sql = updatedSql;

                var rewrittenJson = JsonSerializer.Serialize(keyset, JsonCore.Pretty);
                rewrittenBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(rewrittenJson);

                KeyFileStore.SavePayload(directory, fileName, keyPassword, KeysetPayloadId, rewrittenBytes);

                return UpgradeStepResult.Success(
                    "RewriteKeyFile",
                    $"Key-file SQL payload rewritten with {updatedSql.Count} SQL entr{(updatedSql.Count == 1 ? "y" : "ies")}.");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult.Failure(
                    "RewriteKeyFile",
                    UpgradeFailureCategory.KeyFileRewrite,
                    AppExitCode.UpgradeKeyFileRewriteFailed,
                    "Key-file SQL payload rewrite failed.",
                    ex);
            }
            finally
            {
                if (payloadBytes != null)
                    Array.Clear(payloadBytes, 0, payloadBytes.Length);
                if (rewrittenBytes != null)
                    Array.Clear(rewrittenBytes, 0, rewrittenBytes.Length);
            }
        }

        public UpgradeStepResult ValidateKeyFile(
            string keyFilePath,
            char[] keyPassword,
            UpgradeSqlCatalog catalog)
        {
            byte[]? payloadBytes = null;

            try
            {
                if (catalog == null)
                {
                    return UpgradeStepResult.Failure(
                        "ValidateKeyFile",
                        UpgradeFailureCategory.KeyFileValidation,
                        AppExitCode.UpgradeKeyFileValidationFailed,
                        "Upgrade SQL catalog is required.");
                }

                var requiredSql = catalog.GetSqlMapForKeyFileRebuild().Keys.ToArray();
                if (requiredSql.Length == 0)
                {
                    return UpgradeStepResult.Failure(
                        "ValidateKeyFile",
                        UpgradeFailureCategory.KeyFileValidation,
                        AppExitCode.UpgradeKeyFileValidationFailed,
                        "No required SQL names were available for key-file validation.");
                }

                var (directory, fileName) = SplitKeyFilePath(keyFilePath);
                if (!KeyFileStore.CanOpenAndValidateSchema(directory, fileName, keyPassword, out var reason))
                {
                    return UpgradeStepResult.Failure(
                        "ValidateKeyFile",
                        UpgradeFailureCategory.KeyFileValidation,
                        AppExitCode.UpgradeKeyFileValidationFailed,
                        $"Key-file schema validation failed: {reason}");
                }

                payloadBytes = KeyFileStore.ReadPayloadBytes(directory, fileName, keyPassword, KeysetPayloadId);
                if (payloadBytes.Length == 0)
                {
                    return UpgradeStepResult.Failure(
                        "ValidateKeyFile",
                        UpgradeFailureCategory.KeyFileValidation,
                        AppExitCode.UpgradeKeyFileValidationFailed,
                        "Key-file payload row 1 is empty.");
                }

                byte[] validationBytes = payloadBytes.ToArray();
                if (!KeyProvisioner.ValidateKeysetJson(() => validationBytes))
                {
                    return UpgradeStepResult.Failure(
                        "ValidateKeyFile",
                        UpgradeFailureCategory.KeyFileValidation,
                        AppExitCode.UpgradeKeyFileValidationFailed,
                        "Key-file payload row 1 is not valid keyset JSON.");
                }

                var json = Encoding.UTF8.GetString(payloadBytes);
                var keyset = KeysetJsonV2.Deserialize(json);
                KeysetJsonV2.Validate(keyset, requiredSql);

                return UpgradeStepResult.Success(
                    "ValidateKeyFile",
                    $"Key-file validated with {requiredSql.Length} required SQL entr{(requiredSql.Length == 1 ? "y" : "ies")}.");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult.Failure(
                    "ValidateKeyFile",
                    UpgradeFailureCategory.KeyFileValidation,
                    AppExitCode.UpgradeKeyFileValidationFailed,
                    "Key-file validation failed.",
                    ex);
            }
            finally
            {
                if (payloadBytes != null)
                    Array.Clear(payloadBytes, 0, payloadBytes.Length);
            }
        }

        public UpgradeStepResult RewritePlaceholder() =>
            UpgradeStepResult.Failure(
                "RewriteKeyFile",
                UpgradeFailureCategory.KeyFileRewrite,
                AppExitCode.UpgradeKeyFileRewriteFailed,
                "Key-file rewrite is not implemented yet.");

        public UpgradeStepResult ValidatePlaceholder() =>
            UpgradeStepResult.Failure(
                "ValidateKeyFile",
                UpgradeFailureCategory.KeyFileValidation,
                AppExitCode.UpgradeKeyFileValidationFailed,
                "Key-file validation is not implemented yet.");

        private static (string Directory, string FileName) SplitKeyFilePath(string keyFilePath)
        {
            if (string.IsNullOrWhiteSpace(keyFilePath))
                throw new ArgumentException("Key-file path is required.", nameof(keyFilePath));

            var directory = Path.GetDirectoryName(keyFilePath);
            var fileName = Path.GetFileName(keyFilePath);

            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("Key-file path must include both directory and file name.");

            return (directory, fileName);
        }
    }
}
