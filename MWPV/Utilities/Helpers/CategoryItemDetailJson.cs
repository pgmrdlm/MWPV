// File: Utilities/Helpers/CategoryItemDetailJson.cs
// Scope: MODELS JSON HELPERS (no DB, no encryption)

using System;
using System.Text.Json;
using MWPV.Models;
using Security.Utility; // JsonCore.Default lives here

namespace Utilities.Helpers
{
    internal static class CategoryItemDetailJson
    {
        /// <summary>Deserialize JSON into SecureData. Returns empty payload if null/whitespace.</summary>
        public static SecureData FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new SecureData();

            return JsonSerializer.Deserialize<SecureData>(json, JsonCore.Default)!;
        }

        /// <summary>Serialize SecureData to JSON using JsonCore.Default options.</summary>
        public static string ToJson(SecureData? data)
        {
            data ??= new SecureData();
            data.Meta.ModifiedUtc = DateTime.UtcNow; // model-only convenience
            return JsonSerializer.Serialize(data, JsonCore.Default);
        }

        /// <summary>Create a minimal empty payload JSON (fresh Meta timestamps).</summary>
        public static string CreateEmptyJson() => ToJson(new SecureData());
    }
}
