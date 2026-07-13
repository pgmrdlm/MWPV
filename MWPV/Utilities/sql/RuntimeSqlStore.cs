using System.Collections.ObjectModel;
using MWPV.SqlCatalog;

namespace Utilities.Sql;

public static class RuntimeSqlStore
{
    private static IReadOnlyDictionary<string, string> _snapshot = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static void ReplaceVerified(IEnumerable<VerifiedSqlFile> scripts)
    {
        if (scripts is null) throw new ArgumentNullException(nameof(scripts));
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var script in scripts)
        {
            if (!map.TryAdd(script.CatalogEntry.FileName, script.SqlText))
                throw new InvalidOperationException($"Duplicate verified SQL filename: {script.CatalogEntry.FileName}");
        }
        Interlocked.Exchange(ref _snapshot, new ReadOnlyDictionary<string, string>(map));
    }

    public static string GetSql(string name) => _snapshot.TryGetValue(name, out var sql) && !string.IsNullOrWhiteSpace(sql)
        ? sql : throw new InvalidOperationException($"[SQLCAT] Missing or empty script: {name}");
}
