// File: Services/CategoryItemRepository.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using MWPV.Models;
using Utilities.Helpers; // CategoryItemDetailJson, SecureDataValidator, CategoryItemSecure

namespace MWPV.Services
{
    public sealed class old_CategoryItemRepository
    {
        private readonly Func<DbConnection> _open;
        private readonly string _sqlDir;
        public old_CategoryItemRepository(Func<DbConnection> openConnection, string sqlDirectory)
        { _open = openConnection; _sqlDir = sqlDirectory; }

        // ---- GRID (3 columns per row) ----
        public sealed record GridRow(long? Key1, string? Col1, string? Des1,
                                     long? Key2, string? Col2, string? Des2,
                                     long? Key3, string? Col3, string? Des3);

        public List<GridRow> GridByCategory(long categoryKey)
        {
            using var conn = _open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadSql("s_CategoryItem_SelectGrid.sql");
            Add(cmd, "@Category_Key", DbType.Int64, categoryKey);

            var list = new List<GridRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new GridRow(
                    GetLongN(r, "Key1"), GetStr(r, "Col1"), GetStr(r, "Des1"),
                    GetLongN(r, "Key2"), GetStr(r, "Col2"), GetStr(r, "Des2"),
                    GetLongN(r, "Key3"), GetStr(r, "Col3"), GetStr(r, "Des3")
                ));
            }
            return list;
        }

        // ---- DETAILS ----
        public CategoryItemDetail? GetById(long itemId)
        {
            using var conn = _open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadSql("s_CategoryItem_select_by_id.sql");
            Add(cmd, "@ItemId", DbType.Int64, itemId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var item = new CategoryItemDetail
            {
                CategoryItemKey = r.GetInt64(r.GetOrdinal("ItemId")),
                CategoryKey = r.GetInt64(r.GetOrdinal("Category_Key")),
                DisplayName = GetStr(r, "CI_Name"),
                SecureDataJson = GetStr(r, "CI_SecretData") ?? string.Empty,
                SecureMetaJson = GetStr(r, "CI_SecretMeta")
            };

            // Normalize + compute primary projections
            item.EnsureSecureJson();
            var sd = CategoryItemDetailJson.FromJson(item.SecureDataJson).NormalizeAndValidate();
            var primAcc = sd.Accounts.Find(a => a.IsPrimary);
            var primCard = sd.BankCards.Find(c => c.IsPrimary);
            item.PrimaryAccountAlias = primAcc?.Alias;
            item.PrimaryAccountLast4 = primAcc?.AccountLast4;
            item.PrimaryCardBrand = primCard?.Brand;
            item.PrimaryCardLast4 = primCard?.Last4;
            item.SecureDataJson = CategoryItemDetailJson.ToJson(sd);

            return item;
        }

        public long Insert(CategoryItemDetail item)
        {
            item.EnsureSecureJson();
            var sd = CategoryItemDetailJson.FromJson(item.SecureDataJson).NormalizeAndValidate();
            item.SecureDataJson = CategoryItemDetailJson.ToJson(sd);

            using var conn = _open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadSql("s_CategoryItem_insert.sql");

            Add(cmd, "@CategoryKey", DbType.Int64, item.CategoryKey);
            Add(cmd, "@Name", DbType.String, item.DisplayName ?? string.Empty);
            Add(cmd, "@Description", DbType.String, item.PrimaryAccountAlias); // placeholder text
            Add(cmd, "@SecretData", DbType.String, item.SecureDataJson);
            Add(cmd, "@SecretMeta", DbType.String, item.SecureMetaJson);

            using var r = cmd.ExecuteReader();
            return r.Read() ? r.GetInt64(0) : throw new InvalidOperationException("Insert failed.");
        }

        public void Update(CategoryItemDetail item)
        {
            if (item.CategoryItemKey <= 0) throw new ArgumentException("ItemId missing.");
            item.EnsureSecureJson();
            var sd = CategoryItemDetailJson.FromJson(item.SecureDataJson).NormalizeAndValidate();
            item.SecureDataJson = CategoryItemDetailJson.ToJson(sd);

            using var conn = _open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadSql("s_CategoryItem_update.sql");

            Add(cmd, "@ItemId", DbType.Int64, item.CategoryItemKey);
            Add(cmd, "@Name", DbType.String, item.DisplayName ?? string.Empty);
            Add(cmd, "@Description", DbType.String, item.PrimaryAccountAlias);
            Add(cmd, "@SecretData", DbType.String, item.SecureDataJson);
            Add(cmd, "@SecretMeta", DbType.String, item.SecureMetaJson);

            cmd.ExecuteNonQuery();
        }

        // ---- helpers ----
        private static void Add(DbCommand c, string name, DbType t, object? v)
        { var p = c.CreateParameter(); p.ParameterName = name; p.DbType = t; p.Value = v ?? DBNull.Value; c.Parameters.Add(p); }

        private static string? GetStr(IDataRecord r, string col)
        { int i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }

        private static long? GetLongN(IDataRecord r, string col)
        { int i = r.GetOrdinal(col); return r.IsDBNull(i) ? (long?)null : r.GetInt64(i); }

        private string ReadSql(string file) => File.ReadAllText(Path.Combine(_sqlDir, file));
    }
}
