// File: MWPV/Services/ComboDetailService.cs
using System;
using System.Collections.Generic;
using MWPV.Models;

namespace MWPV.Services
{
    /// <summary>
    /// Central helper for loading ComboDetail rows by combo-type code.
    /// 
    /// For now this is a thin wrapper over LogCatalogService.GetComboDetailsByType
    /// so behavior stays identical while we centralize call sites.
    /// Later we can move the actual DB logic into this class and have
    /// LogCatalogService call into it instead.
    /// </summary>
    public static class ComboDetailService
    {
        /// <summary>
        /// Returns all active ComboDetail rows for the given combo-type code
        /// (e.g. "log_filters"), ordered exactly as provided by the underlying
        /// catalog service.
        /// </summary>
        /// <param name="comboTypeCode">
        /// The logical combo type code (e.g. "log_filters").
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="comboTypeCode"/> is null/empty/whitespace.
        /// </exception>
        public static IReadOnlyList<ComboDetail> GetByType(string comboTypeCode)
        {
            if (string.IsNullOrWhiteSpace(comboTypeCode))
                throw new ArgumentException("Combo type code is required.", nameof(comboTypeCode));

            // TEMP: delegate to existing implementation so behavior remains unchanged.
            // When we refactor, we can move the DB logic here and let
            // LogCatalogService call ComboDetailService instead.
            return LogCatalogService.GetComboDetailsByType(comboTypeCode);
        }
    }
}
