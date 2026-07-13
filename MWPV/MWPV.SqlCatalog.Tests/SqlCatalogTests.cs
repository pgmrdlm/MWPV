using System.Security.Cryptography;
using MWPV.SqlCatalog;
using Xunit;

namespace MWPV.SqlCatalog.Tests;

public sealed class SqlCatalogTests
{
    private static readonly string SqlRoot = FindSqlRoot();

    [Fact]
    public void Catalog_has_safe_unique_lowercase_hashes_and_one_creation_script()
    {
        Assert.Equal(76, TrustedSqlCatalog.Entries.Count);
        Assert.Single(TrustedSqlCatalog.Entries, x => x.Role.HasFlag(SqlScriptRole.DatabaseCreation));
        Assert.All(TrustedSqlCatalog.Entries, x =>
        {
            Assert.Equal(x.FileName, Path.GetFileName(x.FileName));
            Assert.Matches("^[0-9a-f]{64}$", x.Sha256Hex);
        });
        Assert.Equal(TrustedSqlCatalog.Entries.Count, TrustedSqlCatalog.Entries.Select(x => x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Production_inventory_and_raw_byte_hashes_match_compiled_catalog()
    {
        var sourceNames = Directory.EnumerateFiles(SqlRoot, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(x => x == "MWPV_DB_Create.sql" || x!.StartsWith("s_", StringComparison.Ordinal) || x.EndsWith("_Upgrade.sql", StringComparison.OrdinalIgnoreCase))
            .Where(x => x != "CategoryItemTestData.sql").ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(TrustedSqlCatalog.Entries.Select(x => x.FileName).OrderBy(x => x), sourceNames.OrderBy(x => x));
        foreach (var entry in TrustedSqlCatalog.Entries)
            Assert.Equal(entry.Sha256Hex, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(SqlRoot, entry.FileName)))).ToLowerInvariant());
    }

    [Fact]
    public void New_install_requires_complete_verified_package_and_returns_creation_in_payload()
    {
        var valid = AllFiles(); var result = TrustedSqlCatalog.ValidateNewInstallFiles(valid);
        Assert.True(result.Succeeded); Assert.NotNull(result.Value);
        Assert.Equal("MWPV_DB_Create.sql", result.Value!.DatabaseCreationScript.CatalogEntry.FileName);
        Assert.Contains(result.Value.KeyFilePayloadScripts, x => x.CatalogEntry.FileName == "MWPV_DB_Create.sql");
        var missing = TrustedSqlCatalog.ValidateNewInstallFiles(valid.Where(x => x.FileName != "s_Logs_Insert.sql").ToArray());
        Assert.False(missing.Succeeded); Assert.Null(missing.Value); Assert.Contains(missing.Failures, x => x.Code == SqlCatalogFailureCode.MissingRequiredFile);
    }

    [Fact]
    public void New_install_rejects_unknown_duplicates_empty_and_changed_bytes()
    {
        var valid=AllFiles();
        Assert.Contains(TrustedSqlCatalog.ValidateNewInstallFiles(valid.Append(new SqlFileInput("evil.sql", new byte[] { 1 })).ToArray()).Failures, x=>x.Code==SqlCatalogFailureCode.UnexpectedSqlFile);
        Assert.Contains(TrustedSqlCatalog.ValidateNewInstallFiles(valid.Append(valid[0]).ToArray()).Failures, x=>x.Code==SqlCatalogFailureCode.DuplicateFileName);
        Assert.Contains(TrustedSqlCatalog.ValidateNewInstallFiles(valid.Select(x=>x.FileName=="s_Logs_Insert.sql" ? x with { RawBytes=Array.Empty<byte>() }:x).ToArray()).Failures, x=>x.Code==SqlCatalogFailureCode.EmptyFile);
        var changed=valid.Select(x=>x.FileName=="s_Logs_Insert.sql" ? x with { RawBytes=x.RawBytes.ToArray().Append((byte)' ').ToArray() }:x).ToArray();
        Assert.Contains(TrustedSqlCatalog.ValidateNewInstallFiles(changed).Failures, x=>x.Code==SqlCatalogFailureCode.HashMismatch);
    }

    [Fact]
    public void Upgrade_path_is_deterministic_and_fully_validated()
    {
        var result=TrustedSqlCatalog.ValidateUpgradeFiles("1.20", "1.23", AllFiles());
        Assert.True(result.Succeeded); Assert.Equal(["v01.20_v01.21_Upgrade.sql","v01.21_v01.22_Upgrade.sql","v01.22_v01.23_Upgrade.sql"], result.Value!.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName));
        Assert.True(TrustedSqlCatalog.ValidateUpgradeFiles("1.23", null, AllFiles()).Succeeded);
        Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.23","1.20",AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.UnsupportedVersionTransition);
    }

    [Fact]
    public void Directory_adapter_ignores_non_sql_and_does_not_recurse()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { foreach(var x in AllFiles()) File.WriteAllBytes(Path.Combine(dir,x.FileName),x.RawBytes.ToArray()); File.WriteAllText(Path.Combine(dir,"ignored.txt"),"x"); Directory.CreateDirectory(Path.Combine(dir,"nested")); File.WriteAllText(Path.Combine(dir,"nested","evil.sql"),"bad"); Assert.True(TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(dir).Succeeded); }
        finally { Directory.Delete(dir,true); }
    }

    private static SqlFileInput[] AllFiles() => TrustedSqlCatalog.Entries.Select(x=>new SqlFileInput(x.FileName,File.ReadAllBytes(Path.Combine(SqlRoot,x.FileName)),x.FileName)).ToArray();
    private static string FindSqlRoot() { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d is not null) { var candidate=Path.Combine(d.FullName,"sql"); if(Directory.Exists(candidate)&&File.Exists(Path.Combine(candidate,"MWPV_DB_Create.sql"))) return candidate; d=d.Parent; } throw new DirectoryNotFoundException("Repository sql folder was not found."); }
}
