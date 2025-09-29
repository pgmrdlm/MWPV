// File: Utilities/Diagnostics/EarlyLogIngestor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MWPV.Services;          // LogCatalogService
using MWPV.Utilities.Json;    // AppJson
using Security.Utility;

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Ingests *.elogp from EarlyLoginFailures.StoreDir into DB with dedupe & quarantine.
    /// Payload JSON is built centrally via AppJson and stored as UTF-8 with PayloadFmt="json".
    /// Forward-only: original payload goes into dto.Context as structured JSON (not string).
    /// </summary>
    public static class EarlyLogIngestor
    {
        public static void IngestAll()
        {
            var dir = EarlyLoginFailures.StoreDir;
            Directory.CreateDirectory(dir);

            var files = Directory.GetFiles(dir, $"*{EarlyLoginFailures.FileExt}", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                Debug.WriteLine("[EarlyIngest] No early files found.");
                return;
            }

            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int total = 0, inserted = 0, dupes = 0, failed = 0, quarantined = 0;

            foreach (var path in files)
            {
                total++;
                try
                {
                    if (!EarlyLoginFailures.TryReadAndDecrypt(path, out var entry, out var reason, out var rawJson, out _))
                    {
                        Quarantine(path, reason ?? "read/decrypt failed");
                        quarantined++;
                        continue;
                    }

                    if (rawJson == null || rawJson.Length == 0)
                    {
                        Quarantine(path, "empty payload");
                        quarantined++;
                        continue;
                    }

                    // Hash of the original raw JSON for dedupe/quarantine signatures
                    var hashHex = Sha256Hex(rawJson);
                    if (!seenHashes.Add(hashHex))
                    {
                        Quarantine(path, "duplicate in same run (hash)");
                        dupes++;
                        continue;
                    }

                    // Timestamps
                    var createdIso = DateTime.UtcNow.ToString("o");
                    DateTime whenDt = entry?.whenUtc ?? DateTime.UtcNow;
                    if (whenDt.Kind != DateTimeKind.Utc) whenDt = whenDt.ToUniversalTime();
                    var whenIso = whenDt.ToString("o");

                    // Convert original decrypted bytes to structured JSON
                    JsonElement contextJson;
                    using (var doc = JsonDocument.Parse(rawJson))
                        contextJson = doc.RootElement.Clone();

                    // Build standardized payload (forward-only: structured Context)
                    var dto = new AppJson.LogPayloadDto
                    {
                        Message = reason ?? "Early login failure",
                        Source = "EarlyIngest",
                        EventCode = "EARLY_FAIL",
                        OccurredUtc = whenDt,
                        Context = contextJson
                    };

                    // Serialize payload
                    var payloadJson = AppJson.SerializeLogPayload(dto);
                    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

                    // Only the fields V3 SQL actually uses
                    var req = new LogCatalogService.RequestV3
                    {
                        Level = "ERROR",
                        Source = "EarlyIngest",
                        EventCode = "EARLY_FAIL",
                        Payload = payloadBytes,
                        PayloadFmt = "json",
                        CreatedUtc = createdIso,
                        WhenUtc = whenIso,
                        StackHash = hashHex,
                        AppVersion = AppVersion(),
                        PayloadVer = 2      // forward-only marker for the improved shape
                    };

                    var id = LogCatalogService.Insert(req);
                    if (id > 0)
                    {
                        Debug.WriteLine($"[EarlyIngest] Inserted id={id} from {Path.GetFileName(path)}");
                        inserted++;
                        SecureDelete(path);
                    }
                    else
                    {
                        Quarantine(path, "insert failed (service returned -1)");
                        quarantined++;
                    }
                }
                catch (Exception ex)
                {
                    Quarantine(path, "ingest exception: " + ex.Message);
                    failed++;
                }
            }

            Debug.WriteLine($"[EarlyIngest] total={total} inserted={inserted} dupes={dupes} quarantined={quarantined} failed={failed}");
        }

        private static string Sha256Hex(byte[] data)
        {
            // swapped to common routine
            return Sha256Common.Hex(data);
        }

        private static void SecureDelete(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 0 && fi.Length <= 1024 * 1024)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                    var zeros = new byte[8192];
                    long remaining = fi.Length;
                    while (remaining > 0)
                    {
                        var w = (int)Math.Min(zeros.Length, remaining);
                        fs.Write(zeros, 0, w);
                        remaining -= w;
                    }
                    fs.Flush(true);
                }
            }
            catch { }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void Quarantine(string path, string? reason)
        {
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);
                var dest = Path.Combine(EarlyLoginFailures.QuarantineDir, Path.GetFileName(path));
                File.Move(path, dest, overwrite: true);

                if (!string.IsNullOrWhiteSpace(reason))
                    File.WriteAllText(dest + ".reason.txt", reason);
            }
            catch { }
        }

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }
    }
}
