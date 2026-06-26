// File: MWPV/Services/ComboDetailService.cs
using System;
using System.Collections.Generic;
using MWPV.Models;
using Utilities.Helpers;   // DatabaseHelper
using Utilities.Sql;       // SecureSql

namespace MWPV.Services
{
    /// <summary>
    /// Central helper for loading ComboDetail rows.
    /// </summary>
    public static class ComboDetailService
    {
        /// <summary>
        /// Preferred path for logical combo families. Uses the ComboType.Code
        /// value, such as "log_filters", and delegates to LogCatalogService.
        /// </summary>
        public static IReadOnlyList<ComboDetail> GetByType(string comboTypeCode)
        {
            if (string.IsNullOrWhiteSpace(comboTypeCode))
                throw new ArgumentException("Combo type code is required.", nameof(comboTypeCode));

            // Existing behavior – do not change this.
            return LogCatalogService.GetComboDetailsByType(comboTypeCode);
        }

        /// <summary>
        /// Load ComboDetail rows by numeric ComboTypeId using
        /// s_Combo_DetailByTypeId.sql and @ComboTypeId. Retained for existing
        /// reference-combo callers that still store stable numeric ids.
        /// </summary>
        public static IReadOnlyList<ComboDetail> GetByTypeId(int comboTypeId)
        {
            if (comboTypeId <= 0)
                throw new ArgumentOutOfRangeException(nameof(comboTypeId));

            const string SqlName = "s_Combo_DetailByTypeId.sql";

            // Uses Utilities.Sql.SecureSql from the Security.Utility DLL
            var sql = SecureSql.Require(SqlName);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@ComboTypeId", comboTypeId);

            var list = new List<ComboDetail>();

            using var reader = cmd.ExecuteReader();

            // Cache ordinals once
            int ordDet = reader.GetOrdinal("ComboDet");
            int ordTyp = reader.GetOrdinal("ComboTyp");
            int ordSeq = reader.GetOrdinal("Seq");
            int ordCode = reader.GetOrdinal("Code");
            int ordDesc = reader.GetOrdinal("Description");
            int ordAct = reader.GetOrdinal("Active");
            int ordCr = reader.GetOrdinal("CreatedUtc");
            int ordUpd = reader.GetOrdinal("UpdatedUtc");

            while (reader.Read())
            {
                var row = new ComboDetail
                {
                    ComboDet = reader.GetInt32(ordDet),
                    ComboTyp = reader.GetInt32(ordTyp),
                    Seq = reader.GetInt32(ordSeq),
                    Code = reader.GetString(ordCode),
                    Description = reader.IsDBNull(ordDesc)
                        ? string.Empty
                        : reader.GetString(ordDesc),
                    Active = reader.GetInt32(ordAct),
                    CreatedUtc = reader.IsDBNull(ordCr)
                        ? string.Empty
                        : reader.GetString(ordCr),
                    UpdatedUtc = reader.IsDBNull(ordUpd)
                        ? string.Empty
                        : reader.GetString(ordUpd)
                };

                list.Add(row);
            }

            return list;
        }
    }
}
