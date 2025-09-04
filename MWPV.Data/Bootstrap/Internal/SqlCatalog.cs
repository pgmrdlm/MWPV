using System;

namespace MWPV.Data.Internal;

/// <summary>
/// Central resolver for SQL script text. Initialize once at app startup with a resolver
/// that maps a logical name (e.g., "Logs_Insert_V2.sql") to the full SQL string.
/// </summary>
internal static class SqlCatalog
{
    private static Func<string, string>? _resolver;

    /// <summary>
    /// Registers the resolver used to fetch SQL text by name.
    /// </summary>
    public static void Init(Func<string, string> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// Resolves a SQL script by name. Throws if not initialized or if <paramref name="name"/> is invalid.
    /// </summary>
    public static string Require(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("SQL script name cannot be null or empty.", nameof(name));

        var r = _resolver ?? throw new InvalidOperationException("SqlCatalog has not been initialized.");
        return r(name);
    }

    /// <summary>
    /// Attempts to resolve a SQL script by name without throwing on missing/invalid state.
    /// </summary>
    public static bool TryGet(string name, out string? sql)
    {
        if (string.IsNullOrWhiteSpace(name) || _resolver is null)
        {
            sql = null;
            return false;
        }

        sql = _resolver(name);
        return true;
    }
}
