// File: MWPV/Utilities/Diagnostics/SmokeTester.cs
using System;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Utilities.Helpers;          // DatabaseHelper
using MWPV.Services;              // LogCatalogService
using MWPV.Utilities.Json;        // AppJson

namespace MWPV.Utilities.Diagnostics
{
    public static class SmokeTester
    {
        public static void Run()
        {
            // ---- READ: count categories (correct table name) ----
            try
            {
                using var cn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Category;"; // singular table
                var cnt = Convert.ToInt32(cmd.ExecuteScalar());
                Debug.WriteLine($"[SMOKE][READ] Category rows={cnt}");
            }
            catch (SqliteException ex)
            {
                Debug.WriteLine($"[SMOKE][READ][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][READ][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            // ---- WRITE: log a login event ----
            try
            {
                var id = LogCatalogService.InsertLoginEvent();
                Debug.WriteLine($"[SMOKE][WRITE] InsertLoginEvent id={id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][WRITE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            // ---- JSON: central helper sanity (logs payload DTO) ----
            try
            {
                var dto = new AppJson.LogPayloadDto
                {
                    Message = "Smoke test JSON round-trip",
                    Source = "SmokeTester",
                    EventCode = "SMOKE",
                    OccurredUtc = DateTime.UtcNow,
                    User = Environment.UserName
                };

                // serialize (pretty for readability in Debug)
                var json = AppJson.SerializeLogPayload(dto, pretty: true);
                Debug.WriteLine($"[SMOKE][JSON] payload={json}");

                // deserialize
                var ok = AppJson.TryDeserializeLogPayload(json, out var roundTrip);
                Debug.WriteLine($"[SMOKE][JSON] roundtrip-ok={ok} msg={roundTrip?.Message}");

                // (placeholder) encrypted path – currently no-op until we wire AES
                var enc = AppJson.SerializeEncryptedLogPayload(dto);
                var dec = AppJson.DeserializeEncryptedLogPayload(enc);
                Debug.WriteLine($"[SMOKE][JSON] enc-rt-ok={(dec?.Message == dto.Message)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][JSON][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
