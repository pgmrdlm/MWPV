using System.Security.Cryptography;
using System.Text;
using MWPV.SqlCatalog;
using Xunit;

namespace MWPV.SqlCatalog.Tests;

public sealed class FocusedCoverageTests
{
    private static readonly string SqlRoot = FindSqlRoot();

    [Fact] public void Catalog_contains_all_51_operational_scripts() => Assert.Equal(51, TrustedSqlCatalog.Entries.Count(x => x.Role == SqlScriptRole.NormalOperational));
    [Fact] public void Catalog_contains_all_24_supported_upgrade_scripts() => Assert.Equal(24, TrustedSqlCatalog.Entries.Count(x => x.Role == SqlScriptRole.Upgrade));
    [Fact] public void Catalog_excludes_test_and_obsolete_creation_scripts() { Assert.DoesNotContain(TrustedSqlCatalog.Entries, x => x.FileName == "CategoryItemTestData.sql"); Assert.DoesNotContain(TrustedSqlCatalog.Entries, x => x.FileName.StartsWith("V", StringComparison.OrdinalIgnoreCase) && x.FileName.Contains("MWPV_DB_Create")); }
    [Fact] public void Catalog_upgrade_transitions_are_unique_and_forward() { var upgrades=TrustedSqlCatalog.Entries.Where(x=>x.Role==SqlScriptRole.Upgrade).ToArray(); Assert.Equal(upgrades.Length, upgrades.Select(x=>$"{x.UpgradeFromVersion}-{x.UpgradeToVersion}").Distinct().Count()); Assert.All(upgrades,x=>Assert.True(x.UpgradeFromVersion!.Value.CompareTo(x.UpgradeToVersion!.Value)<0)); }
    [Fact] public void Catalog_order_is_stable_and_entries_view_is_read_only() { Assert.Equal(TrustedSqlCatalog.Entries.OrderBy(x=>x.StableOrder).Select(x=>x.FileName), TrustedSqlCatalog.Entries.Select(x=>x.FileName)); Assert.False(TrustedSqlCatalog.Entries is IList<SqlCatalogEntry> { IsReadOnly: false }); }

    [Fact] public void Missing_creation_file_returns_no_package() => AssertFailure(Without("MWPV_DB_Create.sql"), SqlCatalogFailureCode.MissingRequiredFile);
    [Fact] public void Missing_operational_file_returns_no_package() => AssertFailure(Without("s_Logs_Insert.sql"), SqlCatalogFailureCode.MissingRequiredFile);
    [Fact] public void Case_only_duplicate_returns_no_package() { var files=AllFiles(); var input=files.Append(files.Single(x=>x.FileName=="s_Logs_Insert.sql") with { FileName="S_LOGS_INSERT.SQL" }).ToArray(); AssertFailure(input,SqlCatalogFailureCode.DuplicateFileName); }
    [Fact] public void Invalid_utf8_bytes_do_not_produce_a_package() { var input=AllFiles().Select(x=>x.FileName=="s_Logs_Insert.sql" ? x with { RawBytes=new byte[] { 0xff, 0xfe } } : x).ToArray(); Assert.False(TrustedSqlCatalog.ValidateNewInstallFiles(input).Succeeded); Assert.Null(TrustedSqlCatalog.ValidateNewInstallFiles(input).Value); }
    [Fact] public void New_install_order_is_deterministic() { var a=TrustedSqlCatalog.ValidateNewInstallFiles(AllFiles()).Value!; var b=TrustedSqlCatalog.ValidateNewInstallFiles(AllFiles().Reverse().ToArray()).Value!; Assert.Equal(a.KeyFilePayloadScripts.Select(x=>x.CatalogEntry.FileName),b.KeyFilePayloadScripts.Select(x=>x.CatalogEntry.FileName)); }

    [Fact] public void Upgrade_to_123_without_target_uses_highest_reachable_version() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20",null,AllFiles()); Assert.True(r.Succeeded); Assert.Equal("1.23",r.Value!.TargetVersion.ToString()); }
    [Fact] public void Upgrade_rejects_unsupported_current_version() => Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("9.99",null,AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.UnsupportedVersionTransition);
    [Fact] public void Upgrade_rejects_unsupported_target_version() => Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.24",AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.NoValidUpgradePath);
    [Fact] public void Missing_upgrade_script_returns_no_package() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",Without("v01.22_v01.23_Upgrade.sql")); Assert.False(r.Succeeded); Assert.Null(r.Value); Assert.Contains(r.Failures,x=>x.Code==SqlCatalogFailureCode.MissingRequiredFile); }
    [Fact] public void Missing_upgrade_payload_script_returns_no_package() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",Without("s_Logs_Insert.sql")); Assert.False(r.Succeeded); Assert.Null(r.Value); }
    [Fact] public void Altered_later_upgrade_script_returns_no_package() { var files=AllFiles().Select(x=>x.FileName=="v01.22_v01.23_Upgrade.sql" ? x with { RawBytes=x.RawBytes.ToArray().Append((byte)' ').ToArray() } :x).ToArray(); var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",files); Assert.False(r.Succeeded); Assert.Null(r.Value); Assert.Empty(r.Value?.OrderedUpgradeScripts ?? []); }
    [Fact] public void Upgrade_order_is_deterministic() { var a=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",AllFiles()).Value!; var b=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",AllFiles().Reverse().ToArray()).Value!; Assert.Equal(a.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName),b.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName)); }

    [Theory]
    [InlineData("character")][InlineData("whitespace")][InlineData("comment")][InlineData("lf-crlf")][InlineData("crlf-lf")][InlineData("bom-added")][InlineData("bom-removed")][InlineData("encoding")]
    public void Raw_byte_changes_produce_different_sha256(string change)
    { var original=change is "crlf-lf" or "bom-removed" ? new byte[]{0xef,0xbb,0xbf}.Concat(Encoding.UTF8.GetBytes("select 1;\r\n-- comment\r\n")).ToArray() : Encoding.UTF8.GetBytes("select 1;\n-- comment\n"); var changed=change switch { "character"=>Encoding.UTF8.GetBytes("select 2;\n-- comment\n"), "whitespace"=>Encoding.UTF8.GetBytes("select 1; \n-- comment\n"), "comment"=>Encoding.UTF8.GetBytes("select 1;\n-- changed\n"), "lf-crlf"=>Encoding.UTF8.GetBytes("select 1;\r\n-- comment\r\n"), "crlf-lf"=>Encoding.UTF8.GetBytes("select 1;\n-- comment\n"), "bom-added"=>new byte[]{0xef,0xbb,0xbf}.Concat(original).ToArray(), "bom-removed"=>original[3..], "encoding"=>Encoding.Unicode.GetBytes("select 1;\n-- comment\n"), _=>throw new ArgumentOutOfRangeException(nameof(change)) }; Assert.NotEqual(Convert.ToHexString(SHA256.HashData(original)),Convert.ToHexString(SHA256.HashData(changed))); }

    [Fact] public void Missing_directory_returns_io_failure() => Assert.Contains(TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())).Failures,x=>x.Code==SqlCatalogFailureCode.IoFailure);
    [Fact] public void Directory_unknown_sql_uses_same_rejection() { var dir=MakeDirectory(includeAll:true); try { File.WriteAllText(Path.Combine(dir,"unknown.sql"),"select 1;"); Assert.Contains(TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(dir).Failures,x=>x.Code==SqlCatalogFailureCode.UnexpectedSqlFile); } finally { Directory.Delete(dir,true); } }

    private static void AssertFailure(SqlFileInput[] files, SqlCatalogFailureCode code) { var result=TrustedSqlCatalog.ValidateNewInstallFiles(files); Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Contains(result.Failures,x=>x.Code==code); }
    private static SqlFileInput[] Without(string name) => AllFiles().Where(x=>x.FileName!=name).ToArray();
    private static SqlFileInput[] AllFiles()=>TrustedSqlCatalog.Entries.Select(x=>new SqlFileInput(x.FileName,File.ReadAllBytes(Path.Combine(SqlRoot,x.FileName)))).ToArray();
    private static string MakeDirectory(bool includeAll) { var d=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); if(includeAll)foreach(var f in AllFiles())File.WriteAllBytes(Path.Combine(d,f.FileName),f.RawBytes.ToArray()); return d; }
    private static string FindSqlRoot() { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d is not null) { var c=Path.Combine(d.FullName,"sql"); if(File.Exists(Path.Combine(c,"MWPV_DB_Create.sql")))return c; d=d.Parent; } throw new DirectoryNotFoundException(); }
}
