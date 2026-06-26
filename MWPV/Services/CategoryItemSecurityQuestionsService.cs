using Microsoft.Data.Sqlite;
using MWPV.Models;
using Security.Utility.Crypto.Fields;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Utilities.Helpers;
using Utilities.Sql;

namespace MWPV.Services
{
    /// <summary>
    /// Persistence service for CategoryItemSecurityQuestions.
    /// Sensitive values are encrypted at rest and plaintext answers are only returned
    /// by explicit detail/edit paths.
    /// </summary>
    public static class CategoryItemSecurityQuestionsService
    {
        private const string Purpose_CISQ_Question = "CISQ.Question";
        private const string Purpose_CISQ_Answer = "CISQ.Answer";

        private const string Sql_SelectActiveByItemId = "s_CategoryItemSecurityQuestions_select_by_itemid.sql";
        private const string Sql_SelectAllByItemId = "s_CategoryItemSecurityQuestions_select_all_by_itemid.sql";
        private const string Sql_SelectByItemIdAndId = "s_CategoryItemSecurityQuestions_select_by_itemid_and_id.sql";
        private const string Sql_Insert = "s_CategoryItemSecurityQuestions_insert.sql";
        private const string Sql_Update = "s_CategoryItemSecurityQuestions_update.sql";
        private const string Sql_Deactivate = "s_CategoryItemSecurityQuestions_deactivate.sql";

        public sealed class SecurityQuestionListRow
        {
            public long Id { get; init; }
            public long ItemId { get; init; }
            public int Seq { get; init; }
            public string QuestionPlain { get; init; } = string.Empty;
            public string AnswerMasked { get; init; } = string.Empty;
            public bool IsActive { get; init; }
            public long CreatedAtUtcSeconds { get; init; }
            public long UpdatedAtUtcSeconds { get; init; }
        }

        public sealed class SecurityQuestionDetailRow
        {
            public long Id { get; init; }
            public long ItemId { get; init; }
            public int Seq { get; init; }
            public string QuestionPlain { get; init; } = string.Empty;
            public string AnswerPlain { get; init; } = string.Empty;
            public bool IsActive { get; init; }
            public long CreatedAtUtcSeconds { get; init; }
            public long UpdatedAtUtcSeconds { get; init; }
        }

        public static IReadOnlyList<SecurityQuestionListRow> LoadSecurityQuestionListRowsByItemId(long itemId)
        {
            var sourceRows = LoadCategoryItemSecurityQuestionsByItemId(itemId);
            var list = new List<SecurityQuestionListRow>(sourceRows.Count);

            foreach (var row in sourceRows)
            {
                _ = TryDecryptUtf8(Purpose_CISQ_Question, row.Question, out string? questionPlain);
                _ = TryDecryptUtf8(Purpose_CISQ_Answer, row.Answer, out string? answerPlain);

                list.Add(new SecurityQuestionListRow
                {
                    Id = row.Id,
                    ItemId = row.ItemId,
                    Seq = row.Seq,
                    QuestionPlain = questionPlain ?? string.Empty,
                    AnswerMasked = MaskAnswer(answerPlain),
                    IsActive = row.IsActive,
                    CreatedAtUtcSeconds = row.CreatedAtUnix,
                    UpdatedAtUtcSeconds = row.UpdatedAtUnix
                });
            }

            return list;
        }

        public static IReadOnlyList<CategoryItemSecurityQuestion> LoadCategoryItemSecurityQuestionsByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            return LoadCategoryItemSecurityQuestionsByItemIdCore(
                itemId: itemId,
                sqlAssetName: Sql_SelectActiveByItemId,
                errorContext: "Error loading active CategoryItemSecurityQuestions by ItemId");
        }

        public static IReadOnlyList<CategoryItemSecurityQuestion> LoadAllCategoryItemSecurityQuestionsByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            return LoadCategoryItemSecurityQuestionsByItemIdCore(
                itemId: itemId,
                sqlAssetName: Sql_SelectAllByItemId,
                errorContext: "Error loading all CategoryItemSecurityQuestions by ItemId");
        }

        public static CategoryItemSecurityQuestion? LoadCategoryItemSecurityQuestionByItemIdAndId(long itemId, long id)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0.");

            try
            {
                var sql = LoadSqlRequired(Sql_SelectByItemIdAndId);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);
                AddInt64(cmd, "@Id", id);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return null;

                return MapSecurityQuestionRow(r);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading CategoryItemSecurityQuestion by ItemId and Id");
                return null;
            }
        }

        public static SecurityQuestionDetailRow? LoadSecurityQuestionDetailByItemIdAndId(long itemId, long id)
        {
            var row = LoadCategoryItemSecurityQuestionByItemIdAndId(itemId, id);
            if (row == null)
                return null;

            if (!TryDecryptUtf8(Purpose_CISQ_Question, row.Question, out string? questionPlain) ||
                string.IsNullOrWhiteSpace(questionPlain))
            {
                return null;
            }

            if (!TryDecryptUtf8(Purpose_CISQ_Answer, row.Answer, out string? answerPlain) ||
                string.IsNullOrEmpty(answerPlain))
            {
                return null;
            }

            return new SecurityQuestionDetailRow
            {
                Id = row.Id,
                ItemId = row.ItemId,
                Seq = row.Seq,
                QuestionPlain = questionPlain,
                AnswerPlain = answerPlain,
                IsActive = row.IsActive,
                CreatedAtUtcSeconds = row.CreatedAtUnix,
                UpdatedAtUtcSeconds = row.UpdatedAtUnix
            };
        }

        public static long InsertCategoryItemSecurityQuestion(
            long itemId,
            int seq,
            byte[] questionCipher,
            byte[] answerCipher,
            bool isActive)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (seq < 0)
                throw new ArgumentOutOfRangeException(nameof(seq), "seq cannot be negative.");
            if (questionCipher is null || questionCipher.Length == 0)
                throw new InvalidOperationException("Question is required for CategoryItemSecurityQuestions insert.");
            if (answerCipher is null || answerCipher.Length == 0)
                throw new InvalidOperationException("Answer is required for CategoryItemSecurityQuestions insert.");

            try
            {
                var sql = LoadSqlRequired(Sql_Insert);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);
                AddInt32(cmd, "@Seq", seq);
                AddBlob(cmd, "@Question", questionCipher);
                AddBlob(cmd, "@Answer", answerCipher);
                AddInt32(cmd, "@IsActive", isActive ? 1 : 0);

                var scalar = cmd.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value)
                    throw new InvalidOperationException("CategoryItemSecurityQuestion insert failed (no Id returned).");

                return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting CategoryItemSecurityQuestion");
                return 0;
            }
        }

        public static long InsertCategoryItemSecurityQuestionFromUi(
            long itemId,
            int seq,
            string questionPlain,
            string answerPlain,
            bool isActive = true)
        {
            byte[]? questionCipher = EncryptNullableUtf8(Purpose_CISQ_Question, questionPlain);
            byte[]? answerCipher = EncryptNullableUtf8(Purpose_CISQ_Answer, answerPlain);

            if (questionCipher is null || questionCipher.Length == 0)
                throw new InvalidOperationException("Question is required for CategoryItemSecurityQuestions insert.");
            if (answerCipher is null || answerCipher.Length == 0)
                throw new InvalidOperationException("Answer is required for CategoryItemSecurityQuestions insert.");

            try
            {
                return InsertCategoryItemSecurityQuestion(
                    itemId: itemId,
                    seq: seq,
                    questionCipher: questionCipher,
                    answerCipher: answerCipher,
                    isActive: isActive);
            }
            finally
            {
                Array.Clear(questionCipher, 0, questionCipher.Length);
                Array.Clear(answerCipher, 0, answerCipher.Length);
            }
        }

        public static int UpdateCategoryItemSecurityQuestion(
            long id,
            long itemId,
            int seq,
            byte[] questionCipher,
            byte[] answerCipher,
            bool isActive)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0.");
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (seq < 0)
                throw new ArgumentOutOfRangeException(nameof(seq), "seq cannot be negative.");
            if (questionCipher is null || questionCipher.Length == 0)
                throw new InvalidOperationException("Question is required for CategoryItemSecurityQuestions update.");
            if (answerCipher is null || answerCipher.Length == 0)
                throw new InvalidOperationException("Answer is required for CategoryItemSecurityQuestions update.");

            try
            {
                var sql = LoadSqlRequired(Sql_Update);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@Id", id);
                AddInt64(cmd, "@ItemId", itemId);
                AddInt32(cmd, "@Seq", seq);
                AddBlob(cmd, "@Question", questionCipher);
                AddBlob(cmd, "@Answer", answerCipher);
                AddInt32(cmd, "@IsActive", isActive ? 1 : 0);

                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error updating CategoryItemSecurityQuestion");
                return 0;
            }
        }

        public static int UpdateCategoryItemSecurityQuestionFromUi(
            long id,
            long itemId,
            int seq,
            string questionPlain,
            string answerPlain,
            bool isActive)
        {
            byte[]? questionCipher = EncryptNullableUtf8(Purpose_CISQ_Question, questionPlain);
            byte[]? answerCipher = EncryptNullableUtf8(Purpose_CISQ_Answer, answerPlain);

            if (questionCipher is null || questionCipher.Length == 0)
                throw new InvalidOperationException("Question is required for CategoryItemSecurityQuestions update.");
            if (answerCipher is null || answerCipher.Length == 0)
                throw new InvalidOperationException("Answer is required for CategoryItemSecurityQuestions update.");

            try
            {
                return UpdateCategoryItemSecurityQuestion(
                    id: id,
                    itemId: itemId,
                    seq: seq,
                    questionCipher: questionCipher,
                    answerCipher: answerCipher,
                    isActive: isActive);
            }
            finally
            {
                Array.Clear(questionCipher, 0, questionCipher.Length);
                Array.Clear(answerCipher, 0, answerCipher.Length);
            }
        }

        public static int DeactivateCategoryItemSecurityQuestion(long id, long itemId)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0.");
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            try
            {
                var sql = LoadSqlRequired(Sql_Deactivate);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@Id", id);
                AddInt64(cmd, "@ItemId", itemId);

                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error deactivating CategoryItemSecurityQuestion");
                return 0;
            }
        }

        public static int GetNextSeqForItem(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            int maxSeq = 0;
            foreach (var row in LoadAllCategoryItemSecurityQuestionsByItemId(itemId))
            {
                if (row.Seq > maxSeq)
                    maxSeq = row.Seq;
            }

            return maxSeq + 1;
        }

        private static IReadOnlyList<CategoryItemSecurityQuestion> LoadCategoryItemSecurityQuestionsByItemIdCore(
            long itemId,
            string sqlAssetName,
            string errorContext)
        {
            var rows = new List<CategoryItemSecurityQuestion>();

            try
            {
                var sql = LoadSqlRequired(sqlAssetName);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                    rows.Add(MapSecurityQuestionRow(r));
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, errorContext);
            }

            return rows;
        }

        private static CategoryItemSecurityQuestion MapSecurityQuestionRow(SqliteDataReader r)
        {
            var row = new CategoryItemSecurityQuestion
            {
                Id = SafeGetInt32(r, r.GetOrdinal("Id")),
                ItemId = SafeGetInt32(r, r.GetOrdinal("ItemId")),
                Seq = SafeGetInt32(r, r.GetOrdinal("Seq")),
                Question = ReadBlobNullable(r, r.GetOrdinal("Question")) ?? Array.Empty<byte>(),
                Answer = ReadBlobNullable(r, r.GetOrdinal("Answer")) ?? Array.Empty<byte>(),
                IsActive = r.IsDBNull(r.GetOrdinal("IsActive")) || SafeGetInt32(r, r.GetOrdinal("IsActive")) == 1,
                CreatedAtUnix = r.IsDBNull(r.GetOrdinal("CreatedAtUtcSeconds")) ? 0 : SafeGetInt64(r, r.GetOrdinal("CreatedAtUtcSeconds")),
                UpdatedAtUnix = r.IsDBNull(r.GetOrdinal("UpdatedAtUtcSeconds")) ? 0 : SafeGetInt64(r, r.GetOrdinal("UpdatedAtUtcSeconds"))
            };

            _ = TryDecryptUtf8(Purpose_CISQ_Question, row.Question, out string? questionPlain);
            row.QuestionPlain = questionPlain ?? string.Empty;
            row.MarkClean();

            return row;
        }

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        private static byte[]? ReadBlobNullable(SqliteDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal))
                return null;

            try
            {
                return r.GetFieldValue<byte[]>(ordinal);
            }
            catch
            {
                var v = r.GetValue(ordinal);
                return v as byte[];
            }
        }

        private static int SafeGetInt32(SqliteDataReader r, int ordinal)
        {
            var v = r.GetValue(ordinal);

            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            if (v is short s) return s;
            if (v is byte b) return b;

            if (v is string str && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        private static long SafeGetInt64(SqliteDataReader r, int ordinal)
        {
            var v = r.GetValue(ordinal);

            if (v is long l) return l;
            if (v is int i) return i;

            if (v is string str && long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        private static byte[]? EncryptNullableUtf8(string purpose, string? plain)
        {
            if (string.IsNullOrWhiteSpace(plain))
                return null;

            byte[] bytes = Encoding.UTF8.GetBytes(plain.Trim());

            try
            {
                return FieldAesCrypto.EncryptBytes(
                    masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                    purpose: purpose,
                    plaintext: bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool TryDecryptUtf8(string purpose, byte[]? cipherBlob, out string? plain)
        {
            plain = null;

            if (cipherBlob is null || cipherBlob.Length == 0)
                return true;

            try
            {
                if (!FieldAesCrypto.TryDecryptBytes(
                        masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                        purpose: purpose,
                        blob: cipherBlob,
                        out var plainBytes))
                {
                    return false;
                }

                try
                {
                    plain = Encoding.UTF8.GetString(plainBytes);
                    return true;
                }
                finally
                {
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
            catch
            {
                return false;
            }
        }

        private static string MaskAnswer(string? answerPlain)
        {
            return string.IsNullOrEmpty(answerPlain) ? string.Empty : "***";
        }

        private static void AddBlob(SqliteCommand cmd, string name, byte[]? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Blob;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt32(SqliteCommand cmd, string name, int value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt64(SqliteCommand cmd, string name, long value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
    }
}
