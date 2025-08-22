// Utilities/Sql/SqlParameterHelper.cs
using Microsoft.Data.Sqlite;
using System;

namespace Utilities.Sql
{
    /// <summary>
    /// Ensures parameters are always present on a SqliteCommand,
    /// using DBNull.Value when the app value is null.
    /// </summary>
    public static class SqlParameterHelper
    {
        public static void AddNullable(SqliteCommand cmd, string name, object? value)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Parameter name required.", nameof(name));

            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public static void AddNullableBool(SqliteCommand cmd, string name, bool? value)
            => AddNullable(cmd, name, value.HasValue ? (object)(value.Value ? 1 : 0) : DBNull.Value);

        public static void AddNullableDateTime(SqliteCommand cmd, string name, DateTime? valueUtc)
            => AddNullable(cmd, name, valueUtc.HasValue ? valueUtc.Value.ToUniversalTime().ToString("o") : DBNull.Value);
    }
}
