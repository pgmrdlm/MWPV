using System.Text.Json;
using System.Text.Json.Serialization;

namespace Security.Utility
{
    /// <summary>
    /// Centralized JSON serializer options.
    /// Use this instead of creating new JsonSerializerOptions.
    /// </summary>
    public static class JsonCore
    {
        /// <summary>
        /// Default options: camelCase, enums as strings, ignore nulls, strict handling.
        /// </summary>
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            }
        };

        /// <summary>
        /// Pretty-printed version of Default (for human-readable JSON).
        /// </summary>
        public static readonly JsonSerializerOptions Pretty = new(Default)
        {
            WriteIndented = true
        };
    }
}
