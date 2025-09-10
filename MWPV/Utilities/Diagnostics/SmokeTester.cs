// Utilities/Diagnostics/SmokeTester.cs
using System;
using System.Diagnostics;
using System.Text.Json;
using MWPV.Services;        // LogCatalogService
using MWPV.Utilities.Json;  // AppJson

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Lightweight smoke checks run at startup:
    /// - Basic category read (elsewhere, if you keep it)
    /// - JSON round-trip using AppJson
    /// - Append a simple SMOKE log row to the DB
    /// </summary>
    public static class SmokeTester
    {
        public static void Run()
        {
            try
            {
                JsonRoundTrip();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][JSON][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                WriteSmokeLog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][WRITE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void JsonRoundTrip()
        {
            var dto = new AppJson.LogPayloadDto
            {
                Message = "Smoke test JSON round-trip",
                Source = "SmokeTester",
                EventCode = "SMOKE",
                OccurredUtc = DateTime.UtcNow,
                // No User field in your current LogPayloadDto
                // Context left null
            };

            // Serialize with the canonical options
            string json = AppJson.SerializeLogPayload(dto);
            Debug.WriteLine("[SMOKE][JSON] payload=\n" + json);

            // Parse with System.Text.Json (validates structure)
            using var doc = JsonDocument.Parse(json);
            bool hasMsg = doc.RootElement.TryGetProperty("message", out var msgProp);
            string? msg = hasMsg ? msgProp.GetString() : null;

            // And deserialize back into the DTO using your helper
            var roundTrip = AppJson.DeserializeLogPayload(json);

            bool ok = roundTrip != null && roundTrip.Message == dto.Message;
            Debug.WriteLine($"[SMOKE][JSON] roundtrip-ok={ok} msg={msg ?? "<null>"}");

            // If you still want an “encrypted” path later, wire it up in AppJson and
            // add a separate check here. For now we avoid calling non-existent helpers.
        }

        private static void WriteSmokeLog()
        {
            var dto = new AppJson.LogPayloadDto
            {
                Message = "Smoke test JSON round-trip",
                Source = "SmokeTester",
                EventCode = "SMOKE",
                OccurredUtc = DateTime.UtcNow
            };

            long id = LogCatalogService.AppendJson(
                level: "INFO",
                source: "SmokeTester",
                eventCode: "SMOKE",
                dto: dto
            );

            Debug.WriteLine($"[SMOKE][WRITE] InsertLoginEvent id={id}");
        }
    }
}
