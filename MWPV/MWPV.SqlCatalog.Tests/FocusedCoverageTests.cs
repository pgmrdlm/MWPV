using System.Security.Cryptography;
using System.Text;
using MWPV.SqlCatalog;
using Xunit;

namespace MWPV.SqlCatalog.Tests;

public sealed class FocusedCoverageTests
{
    private static readonly string SqlRoot = FindSqlRoot();

    [Fact] public void Catalog_contains_all_53_operational_scripts() => Assert.Equal(53, TrustedSqlCatalog.Entries.Count(x => x.Role == SqlScriptRole.NormalOperational));
    [Fact] public void Catalog_contains_all_25_supported_upgrade_scripts() => Assert.Equal(25, TrustedSqlCatalog.Entries.Count(x => x.Role == SqlScriptRole.Upgrade));
    [Fact] public void Catalog_upgrade_transitions_are_unique_and_forward() { var upgrades=TrustedSqlCatalog.Entries.Where(x=>x.Role==SqlScriptRole.Upgrade).ToArray(); Assert.Equal(upgrades.Length, upgrades.Select(x=>$"{x.UpgradeFromVersion}-{x.UpgradeToVersion}").Distinct().Count()); Assert.All(upgrades,x=>Assert.True(x.UpgradeFromVersion!.Value.CompareTo(x.UpgradeToVersion!.Value)<0)); }
    [Fact] public void Catalog_order_is_stable_and_entries_view_is_read_only() { Assert.Equal(TrustedSqlCatalog.Entries.OrderBy(x=>x.StableOrder).Select(x=>x.FileName), TrustedSqlCatalog.Entries.Select(x=>x.FileName)); Assert.False(TrustedSqlCatalog.Entries is IList<SqlCatalogEntry> { IsReadOnly: false }); }

    [Fact] public void Missing_creation_file_returns_no_package() => AssertFailure(Without("MWPV_DB_Create.sql"), SqlCatalogFailureCode.MissingRequiredFile);
    [Fact] public void Missing_operational_file_returns_no_package() => AssertFailure(Without("s_Logs_Insert.sql"), SqlCatalogFailureCode.MissingRequiredFile);
    [Fact] public void Exact_duplicate_required_filename_returns_no_package() { var files=AllFiles(); AssertFailure(files.Append(files.Single(x=>x.FileName=="s_Logs_Insert.sql")).ToArray(),SqlCatalogFailureCode.DuplicateFileName); }
    [Fact] public void Case_only_duplicate_returns_no_package() { var files=AllFiles(); var input=files.Append(files.Single(x=>x.FileName=="s_Logs_Insert.sql") with { FileName="S_LOGS_INSERT.SQL" }).ToArray(); AssertFailure(input,SqlCatalogFailureCode.DuplicateFileName); }
    [Fact] public void Invalid_utf8_bytes_do_not_produce_a_package() { var input=AllFiles().Select(x=>x.FileName=="s_Logs_Insert.sql" ? x with { RawBytes=new byte[] { 0xff, 0xfe } } : x).ToArray(); Assert.False(TrustedSqlCatalog.ValidateNewInstallFiles(input).Succeeded); Assert.Null(TrustedSqlCatalog.ValidateNewInstallFiles(input).Value); }
    [Fact] public void New_install_order_is_deterministic() { var a=TrustedSqlCatalog.ValidateNewInstallFiles(AllFiles()).Value!; var b=TrustedSqlCatalog.ValidateNewInstallFiles(AllFiles().Reverse().ToArray()).Value!; Assert.Equal(a.KeyFilePayloadScripts.Select(x=>x.CatalogEntry.FileName),b.KeyFilePayloadScripts.Select(x=>x.CatalogEntry.FileName)); }

    [Fact] public void Upgrade_to_124_without_target_uses_highest_reachable_version() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20",null,AllFiles()); Assert.True(r.Succeeded); Assert.Equal("1.24",r.Value!.TargetVersion.ToString()); }
    [Fact] public void Terminal_124_without_target_returns_empty_no_op_route() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("01.24",null,AllFiles()); Assert.True(r.Succeeded); Assert.Empty(r.Value!.OrderedUpgradeScripts); Assert.Equal("01.24",r.Value.TargetVersion.ToString()); }
    [Fact] public void Terminal_124_with_equivalent_padded_target_returns_empty_no_op_route() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.24","01.24",AllFiles()); Assert.True(r.Succeeded); Assert.Empty(r.Value!.OrderedUpgradeScripts); Assert.Equal("1.24",r.Value.TargetVersion.ToString()); }
    [Fact] public void Upgrade_from_123_to_124_selects_the_migration() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("01.23","1.24",AllFiles()); Assert.True(r.Succeeded); Assert.Equal(["v01.23_v01.24_Upgrade.sql"],r.Value!.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName)); }
    [Fact] public void Unknown_current_version_is_rejected() => Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.99",null,AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.UnsupportedVersionTransition);
    [Fact] public void Terminal_version_rejects_downgrade_and_higher_unreachable_target() { Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.24","1.23",AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.UnsupportedVersionTransition); Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.24","1.25",AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.NoValidUpgradePath); }
    [Fact] public void Synthetic_future_terminal_version_has_no_hard_coded_dependency()
    {
        var edge = new SqlCatalogEntry("v02.00_v02.01_Upgrade.sql", new string('a', 64), SqlScriptRole.Upgrade, false, false, SqlVersion.Parse("2.00"), SqlVersion.Parse("2.01"), 1);
        var result = TrustedSqlCatalog.PlanForVersions([edge], SqlVersion.Parse("02.01"), null);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!);
    }
    [Fact] public void Upgrade_rejects_unsupported_current_version() => Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("9.99",null,AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.UnsupportedVersionTransition);
    [Fact] public void Upgrade_rejects_unsupported_target_version() => Assert.Contains(TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.25",AllFiles()).Failures,x=>x.Code==SqlCatalogFailureCode.NoValidUpgradePath);
    [Fact] public void Missing_upgrade_script_returns_no_package() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",Without("v01.22_v01.23_Upgrade.sql")); Assert.False(r.Succeeded); Assert.Null(r.Value); Assert.Contains(r.Failures,x=>x.Code==SqlCatalogFailureCode.MissingRequiredFile); }
    [Fact] public void Missing_upgrade_payload_script_returns_no_package() { var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",Without("s_Logs_Insert.sql")); Assert.False(r.Succeeded); Assert.Null(r.Value); }
    [Fact] public void Altered_later_upgrade_script_returns_no_package() { var files=AllFiles().Select(x=>x.FileName=="v01.22_v01.23_Upgrade.sql" ? x with { RawBytes=x.RawBytes.ToArray().Append((byte)' ').ToArray() } :x).ToArray(); var r=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",files); Assert.False(r.Succeeded); Assert.Null(r.Value); Assert.Empty(r.Value?.OrderedUpgradeScripts ?? []); }
    [Fact] public void Upgrade_order_is_deterministic() { var a=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",AllFiles()).Value!; var b=TrustedSqlCatalog.ValidateUpgradeFiles("1.20","1.23",AllFiles().Reverse().ToArray()).Value!; Assert.Equal(a.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName),b.OrderedUpgradeScripts.Select(x=>x.CatalogEntry.FileName)); }

    [Theory]
    [InlineData("character")][InlineData("whitespace")][InlineData("comment")][InlineData("lf-crlf")][InlineData("crlf-lf")][InlineData("bom-added")][InlineData("bom-removed")][InlineData("encoding")]
    public void Raw_byte_changes_produce_different_sha256(string change)
    { var original=change is "crlf-lf" or "bom-removed" ? new byte[]{0xef,0xbb,0xbf}.Concat(Encoding.UTF8.GetBytes("select 1;\r\n-- comment\r\n")).ToArray() : Encoding.UTF8.GetBytes("select 1;\n-- comment\n"); var changed=change switch { "character"=>Encoding.UTF8.GetBytes("select 2;\n-- comment\n"), "whitespace"=>Encoding.UTF8.GetBytes("select 1; \n-- comment\n"), "comment"=>Encoding.UTF8.GetBytes("select 1;\n-- changed\n"), "lf-crlf"=>Encoding.UTF8.GetBytes("select 1;\r\n-- comment\r\n"), "crlf-lf"=>Encoding.UTF8.GetBytes("select 1;\n-- comment\n"), "bom-added"=>new byte[]{0xef,0xbb,0xbf}.Concat(original).ToArray(), "bom-removed"=>original[3..], "encoding"=>Encoding.Unicode.GetBytes("select 1;\n-- comment\n"), _=>throw new ArgumentOutOfRangeException(nameof(change)) }; Assert.NotEqual(Convert.ToHexString(SHA256.HashData(original)),Convert.ToHexString(SHA256.HashData(changed))); }

    [Fact] public void Missing_directory_returns_io_failure() => Assert.Contains(TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())).Failures,x=>x.Code==SqlCatalogFailureCode.IoFailure);
    [Fact] public void New_install_directory_ignores_unrelated_sql_and_never_returns_or_packages_it()
    {
        var dir=MakeDirectory(includeAll:true);
        try
        {
            File.WriteAllText(Path.Combine(dir,"unrelated-maintenance.sql"),"select 1;");
            var result=TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(dir);
            Assert.True(result.Succeeded);
            Assert.DoesNotContain(result.Value!.FilesByName.Keys,x=>x.Equals("unrelated-maintenance.sql",StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Value.KeyFilePayloadScripts,x=>x.CatalogEntry.FileName.Equals("unrelated-maintenance.sql",StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public void Upgrade_directory_ignores_unrelated_sql_and_never_returns_or_packages_it()
    {
        var dir=MakeDirectory(includeAll:true);
        try
        {
            File.WriteAllText(Path.Combine(dir,"unrelated-maintenance.sql"),"select 1;");
            var result=TrustedSqlCatalog.LoadAndValidateUpgradeDirectory(dir,"1.20","1.23");
            Assert.True(result.Succeeded);
            Assert.DoesNotContain(result.Value!.FilesByName.Keys,x=>x.Equals("unrelated-maintenance.sql",StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Value.KeyFilePayloadScripts,x=>x.CatalogEntry.FileName.Equals("unrelated-maintenance.sql",StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Value.OrderedUpgradeScripts,x=>x.CatalogEntry.FileName.Equals("unrelated-maintenance.sql",StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public void Upgrade_directory_does_not_load_nonselected_trusted_upgrade_scripts()
    {
        var dir=MakeDirectory(includeAll:true);
        try
        {
            File.AppendAllText(Path.Combine(dir,"v00.00_v01.00_Upgrade.sql")," altered but not selected");
            var result=TrustedSqlCatalog.LoadAndValidateUpgradeDirectory(dir,"1.20","1.23");
            Assert.True(result.Succeeded);
            Assert.DoesNotContain(result.Value!.FilesByName.Keys,x=>x.Equals("v00.00_v01.00_Upgrade.sql",StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public void Directory_still_rejects_missing_required_file()
    {
        var dir=MakeDirectory(includeAll:true);
        try
        {
            File.Delete(Path.Combine(dir,"s_Logs_Insert.sql"));
            var result=TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(dir);
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            Assert.Contains(result.Failures,x=>x.Code==SqlCatalogFailureCode.MissingRequiredFile && x.FileName=="s_Logs_Insert.sql");
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public void Directory_still_rejects_altered_required_file()
    {
        var dir=MakeDirectory(includeAll:true);
        try
        {
            File.AppendAllText(Path.Combine(dir,"s_Logs_Insert.sql")," ");
            var result=TrustedSqlCatalog.LoadAndValidateNewInstallDirectory(dir);
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            Assert.Contains(result.Failures,x=>x.Code==SqlCatalogFailureCode.HashMismatch && x.FileName=="s_Logs_Insert.sql");
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public void Decrypted_payload_inputs_remain_strict_for_unknown_sql()
    {
        var result=TrustedSqlCatalog.ValidateNewInstallFiles(AllFiles().Append(new SqlFileInput("unrelated-maintenance.sql",Encoding.UTF8.GetBytes("select 1;"))).ToArray());
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Failures,x=>x.Code==SqlCatalogFailureCode.UnexpectedSqlFile);
    }

    private static void AssertFailure(SqlFileInput[] files, SqlCatalogFailureCode code) { var result=TrustedSqlCatalog.ValidateNewInstallFiles(files); Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Contains(result.Failures,x=>x.Code==code); }
    private static SqlFileInput[] Without(string name) => AllFiles().Where(x=>x.FileName!=name).ToArray();
    private static SqlFileInput[] AllFiles()=>TrustedSqlCatalog.Entries.Select(x=>new SqlFileInput(x.FileName,File.ReadAllBytes(Path.Combine(SqlRoot,x.FileName)))).ToArray();
    private static string MakeDirectory(bool includeAll) { var d=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); if(includeAll)foreach(var f in AllFiles())File.WriteAllBytes(Path.Combine(d,f.FileName),f.RawBytes.ToArray()); return d; }
    private static string FindSqlRoot() { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d is not null) { var c=Path.Combine(d.FullName,"sql"); if(File.Exists(Path.Combine(c,"MWPV_DB_Create.sql")))return c; d=d.Parent; } throw new DirectoryNotFoundException(); }
}
