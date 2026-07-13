using System.Collections.ObjectModel;

namespace MWPV.SqlCatalog;

[Flags]
public enum SqlScriptRole { None = 0, DatabaseCreation = 1, NormalOperational = 2, Upgrade = 4 }

public enum SqlCatalogFailureCode
{
    MissingRequiredFile, UnexpectedSqlFile, DuplicateFileName, EmptyFile, HashMismatch,
    DecodeFailure, InvalidCatalogDefinition, InvalidVersion, NoValidUpgradePath,
    AmbiguousUpgradePath, UnsupportedVersionTransition, IoFailure
}

public readonly record struct SqlVersion : IComparable<SqlVersion>
{
    private readonly int[]? _parts;
    public string Text { get; }
    private SqlVersion(string text, int[] parts) { Text = text; _parts = parts; }
    public static bool TryParse(string? text, out SqlVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = text.Trim().TrimStart('v', 'V');
        var source = clean.Split('.', StringSplitOptions.None);
        if (source.Length == 0 || source.Any(x => !int.TryParse(x, out _))) return false;
        version = new SqlVersion(clean, source.Select(int.Parse).ToArray()); return true;
    }
    public static SqlVersion Parse(string text) => TryParse(text, out var v) ? v : throw new ArgumentException("Invalid SQL version.", nameof(text));
    public int CompareTo(SqlVersion other)
    {
        for (var i = 0; i < Math.Max(_parts?.Length ?? 0, other._parts?.Length ?? 0); i++)
        {
            var c = (i < (_parts?.Length ?? 0) ? _parts![i] : 0).CompareTo(i < (other._parts?.Length ?? 0) ? other._parts![i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }
    public override string ToString() => Text ?? string.Empty;
}

public sealed record SqlCatalogEntry(string FileName, string Sha256Hex, SqlScriptRole Role,
    bool IncludeInNewInstall, bool IncludeInKeyFilePayload, SqlVersion? UpgradeFromVersion,
    SqlVersion? UpgradeToVersion, int StableOrder);

public sealed record SqlFileInput(string FileName, ReadOnlyMemory<byte> RawBytes, string? SourceDescription = null);
public sealed record SqlCatalogFailure(SqlCatalogFailureCode Code, string? FileName = null,
    string? ExpectedHash = null, string? ActualHash = null, string? Message = null, string? SourceDescription = null);
public sealed class CatalogResult<T>
{
    public bool Succeeded => Failures.Count == 0;
    public T? Value { get; }
    public IReadOnlyList<SqlCatalogFailure> Failures { get; }
    private CatalogResult(T? value, IReadOnlyList<SqlCatalogFailure> failures) { Value = value; Failures = failures; }
    public static CatalogResult<T> Success(T value) => new(value, Array.AsReadOnly(Array.Empty<SqlCatalogFailure>()));
    public static CatalogResult<T> Failure(IEnumerable<SqlCatalogFailure> failures) => new(default, Array.AsReadOnly(failures.ToArray()));
}
public sealed record VerifiedSqlFile(SqlCatalogEntry CatalogEntry, ReadOnlyMemory<byte> RawBytes, string SqlText);
public sealed class VerifiedNewInstallPackage
{
    public VerifiedSqlFile DatabaseCreationScript { get; }
    public IReadOnlyList<VerifiedSqlFile> KeyFilePayloadScripts { get; }
    public IReadOnlyDictionary<string, VerifiedSqlFile> FilesByName { get; }
    internal VerifiedNewInstallPackage(VerifiedSqlFile creation, IEnumerable<VerifiedSqlFile> payload, IEnumerable<VerifiedSqlFile> all)
    { DatabaseCreationScript = creation; KeyFilePayloadScripts = Array.AsReadOnly(payload.ToArray()); FilesByName = new ReadOnlyDictionary<string, VerifiedSqlFile>(all.ToDictionary(x => x.CatalogEntry.FileName, StringComparer.OrdinalIgnoreCase)); }
}
public sealed class VerifiedUpgradePackage
{
    public SqlVersion CurrentVersion { get; } public SqlVersion TargetVersion { get; }
    public IReadOnlyList<VerifiedSqlFile> OrderedUpgradeScripts { get; }
    public IReadOnlyList<VerifiedSqlFile> KeyFilePayloadScripts { get; }
    public IReadOnlyDictionary<string, VerifiedSqlFile> FilesByName { get; }
    internal VerifiedUpgradePackage(SqlVersion current, SqlVersion target, IEnumerable<VerifiedSqlFile> upgrades, IEnumerable<VerifiedSqlFile> payload, IEnumerable<VerifiedSqlFile> all)
    { CurrentVersion=current; TargetVersion=target; OrderedUpgradeScripts=Array.AsReadOnly(upgrades.ToArray()); KeyFilePayloadScripts=Array.AsReadOnly(payload.ToArray()); FilesByName=new ReadOnlyDictionary<string,VerifiedSqlFile>(all.ToDictionary(x=>x.CatalogEntry.FileName,StringComparer.OrdinalIgnoreCase)); }
}
