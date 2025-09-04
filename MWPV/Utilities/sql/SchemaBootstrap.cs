// Utilities/Sql/SchemaBootstrap.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Utilities.Sql
{
    /// <summary>
    /// Runs idempotent log schema steps (Init + Indexes) with preflight checks
    /// so re-running the scripts is quiet and fast.
    /// </summary>
    public static class SchemaBootstrap
    {
        // Set to false if you want zero output even in DEBUG builds.
        private const bool VerboseBootstrap = true;

        /// <summary>
        /// Call this after the DB is open and after SqlCagegory.LoadAll() has loaded scripts into the secure store.
        /// </summary>
        public static void EnsureLogsSchema(SqliteConnection openConn)
        {
            if (openConn == null) throw new ArgumentNullException(nameof(openConn));

            // Scripts may contain their own BEGIN/COMMIT; do NOT wrap in another transaction here.
            ExecScriptIdempotent(openConn, SecureSql.Require("Logs_Init.sql"));
            ExecScriptIdempotent(openConn, SecureSql.Require("Logs_Indexes.sql"));
        }

        private static void ExecScriptIdempotent(SqliteConnection conn, string script)
        {
            var stats = new BootstrapStats();

            foreach (var raw in SplitSqlStatements(script))
            {
                var sql = raw.Trim();
                if (sql.Length == 0) continue;

                // Fast skip for comments/pragma noise (they’re fine to run too)
                if (sql.StartsWith("--") || sql.StartsWith("/*")) continue;

                // Normalize a light uppercase copy for matching while preserving original for execution
                var up = UpperHead(sql, 80);

                // 1) ALTER TABLE ... ADD COLUMN ...
                if (AlterAddCol.TryParse(up, out var tableName, out var columnName))
                {
                    if (ColumnExists(conn, tableName, columnName))
                    {
                        stats.SkippedColumns++;
                        continue;
                    }
                    TryExec(conn, sql, stats);
                    continue;
                }

                // 2) CREATE TABLE [IF NOT EXISTS] <name> ...
                if (up.StartsWith("CREATE TABLE"))
                {
                    var hasIfNotExists = up.Contains("IF NOT EXISTS");
                    var table = ExtractCreateName(sql, "table"); // tolerant name extractor
                    if (!string.IsNullOrEmpty(table))
                    {
                        if (TableExists(conn, table))
                        {
                            stats.SkippedTables++;
                            continue;
                        }
                    }
                    // If the script already uses IF NOT EXISTS, just run it.
                    TryExec(conn, sql, stats);
                    continue;
                }

                // 3) CREATE INDEX [IF NOT EXISTS] <name> ...
                if (up.StartsWith("CREATE INDEX") || up.StartsWith("CREATE UNIQUE INDEX"))
                {
                    var index = ExtractCreateName(sql, "index");
                    if (!string.IsNullOrEmpty(index) && IndexExists(conn, index))
                    {
                        stats.SkippedIndexes++;
                        continue;
                    }
                    TryExec(conn, sql, stats);
                    continue;
                }

                // 4) Everything else: run tolerantly
                TryExec(conn, sql, stats);
            }

#if DEBUG
            if (VerboseBootstrap)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Bootstrap] OK: ran={stats.Ran} skipped: tables={stats.SkippedTables}, cols={stats.SkippedColumns}, indexes={stats.SkippedIndexes}, benignIgnored={stats.BenignIgnored}");
            }
#endif
        }

        private static void TryExec(SqliteConnection conn, string sql, BootstrapStats stats)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            try
            {
                cmd.ExecuteNonQuery();
                stats.Ran++;
            }
            catch (SqliteException ex)
            {
                var msg = (ex.Message ?? string.Empty).ToLowerInvariant();

                // Benign idempotency noise we still tolerate if it slips through.
                bool benign =
                    msg.Contains("duplicate column name") ||
                    msg.Contains("already exists");

                if (benign)
                {
                    stats.BenignIgnored++;
                    // Intentionally NO per-statement log spam.
                    return;
                }

                throw;
            }
        }

        // ---------- Existence checks ----------

        private static bool TableExists(SqliteConnection conn, string name)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteScalar() != null;
        }

        private static bool IndexExists(SqliteConnection conn, string name)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name=$n LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteScalar() != null;
        }

        private static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({QuoteIdent(table)});";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var colName = rd["name"] as string;
                if (string.Equals(colName, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string QuoteIdent(string ident)
        {
            // Minimal quoting for PRAGMA table_info() usage
            if (ident.IndexOfAny(new[] { '"', '\'', '`', ' ', '\t', '\r', '\n' }) >= 0)
                return "\"" + ident.Replace("\"", "\"\"") + "\"";
            return ident;
        }

        // ---------- Statement splitting & parsing helpers ----------

        private static IEnumerable<string> SplitSqlStatements(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                yield break;

            var sb = new StringBuilder();
            bool inSingle = false, inDouble = false, inLineComment = false, inBlockComment = false;

            for (int i = 0; i < script.Length; i++)
            {
                char c = script[i];
                char next = i + 1 < script.Length ? script[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\r' || c == '\n') inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; i++; }
                    continue;
                }

                // Enter comment?
                if (!inSingle && !inDouble)
                {
                    if (c == '-' && next == '-') { inLineComment = true; i++; continue; }
                    if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
                }

                if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append(c); continue; }
                if (!inSingle && c == '"') { inDouble = !inDouble; sb.Append(c); continue; }

                // Statement terminator only when not inside a string
                if (!inSingle && !inDouble && c == ';')
                {
                    var stmt = sb.ToString().Trim();
                    if (stmt.Length > 0) yield return stmt;
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            var tail = sb.ToString().Trim();
            if (tail.Length > 0) yield return tail;
        }

        private static string UpperHead(string s, int take)
        {
            if (s.Length <= take) return s.ToUpperInvariant();
            return s.Substring(0, take).ToUpperInvariant() + s.Substring(take);
        }

        private static string ExtractCreateName(string sql, string kind /* 'table' or 'index' */)
        {
            // Very tolerant extractor: CREATE [UNIQUE] INDEX [IF NOT EXISTS] name ...
            // or CREATE TABLE [IF NOT EXISTS] name ...
            var rx = new Regex($@"^CREATE\s+(?:UNIQUE\s+)?{kind}\s+(?:IF\s+NOT\s+EXISTS\s+)?([^\s(]+)",
                               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var m = rx.Match(sql);
            if (!m.Success) return string.Empty;

            var raw = m.Groups[1].Value.Trim();
            // Strip simple quoting
            if ((raw.StartsWith("\"") && raw.EndsWith("\"")) ||
                (raw.StartsWith("`") && raw.EndsWith("`")) ||
                (raw.StartsWith("[") && raw.EndsWith("]")))
            {
                raw = raw.Substring(1, raw.Length - 2);
            }
            return raw;
        }

        private static class AlterAddCol
        {
            // ALTER TABLE <table> ADD COLUMN <column> ...
            private static readonly Regex Rx =
                new Regex(@"^ALTER\s+TABLE\s+([^\s]+)\s+ADD\s+COLUMN\s+([^\s(]+)",
                          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            public static bool TryParse(string sqlUpperHead, out string table, out string column)
            {
                var m = Rx.Match(sqlUpperHead);
                if (m.Success)
                {
                    table = Unquote(m.Groups[1].Value);
                    column = Unquote(m.Groups[2].Value);
                    return true;
                }
                table = column = string.Empty;
                return false;
            }

            private static string Unquote(string ident)
            {
                ident = ident.Trim();
                if ((ident.StartsWith("\"") && ident.EndsWith("\"")) ||
                    (ident.StartsWith("`") && ident.EndsWith("`")) ||
                    (ident.StartsWith("[") && ident.EndsWith("]")))
                {
                    return ident.Substring(1, ident.Length - 2);
                }
                return ident;
            }
        }

        private sealed class BootstrapStats
        {
            public int Ran;
            public int SkippedTables;
            public int SkippedColumns;
            public int SkippedIndexes;
            public int BenignIgnored;
        }
    }
}
