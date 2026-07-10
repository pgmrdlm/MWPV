using Backup.Utility.Internal;

namespace Backup.Utility.Tests;

public sealed class NameAndMappingTests
{
    [Fact]
    public void ValidTemplateResolvesExpectedName()
    {
        bool ok = BackupNameResolver.TryResolve("MWPV_DB_ccyymmdd_hhmmss", new DateTime(2026, 7, 10, 14, 35, 22), out string name, out _);
        Assert.True(ok);
        Assert.Equal("MWPV_DB_20260710_143522", name);
    }

    [Theory]
    [InlineData("MWPV_DB")]
    [InlineData("ccyymmdd_hhmmss")]
    [InlineData("MWPV_ccyymmdd_hhmmss_more")]
    [InlineData("MWPV_ccyymmdd_hhmmss_ccyymmdd_hhmmss")]
    [InlineData("../MWPV_ccyymmdd_hhmmss")]
    public void InvalidTemplatesAreRejected(string template) =>
        Assert.False(BackupNameResolver.TryResolve(template, DateTime.Now, out _, out _));

    [Theory]
    [InlineData(0, BackupStatus.SuccessNoChanges, true)]
    [InlineData(1, BackupStatus.SuccessCopied, true)]
    [InlineData(2, BackupStatus.SuccessWithWarnings, true)]
    [InlineData(3, BackupStatus.SuccessWithWarnings, true)]
    [InlineData(4, BackupStatus.Incomplete, false)]
    [InlineData(7, BackupStatus.Incomplete, false)]
    [InlineData(8, BackupStatus.Failed, false)]
    [InlineData(15, BackupStatus.Failed, false)]
    [InlineData(16, BackupStatus.FatalFailure, false)]
    public void ExitCodesMapCorrectly(int code, BackupStatus status, bool publish)
    {
        var result = RobocopyExitCodeMapper.Map(code);
        Assert.Equal(status, result.Status);
        Assert.Equal(publish, result.CanPublish);
    }
}
