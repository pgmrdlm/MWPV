using System.Text.Json;
using System.Security.Cryptography;

namespace Backup.Utility.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateAsync_CuratesFilesWritesPortableManifestAndUsesCollisionSuffix()
    {
        using var temp = new TempDirectory();
        string sources = temp.Create("sources"); string root = temp.Create("backups");
        string database = Path.Combine(sources, "MWPV.db"); string key = Path.Combine(sources, "vault.pv");
        File.WriteAllText(database, "database"); File.WriteAllText(key, "key");
        var local = new DateTime(2026, 7, 10, 14, 35, 22, DateTimeKind.Unspecified);
        var now = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        var service = new BackupService(() => now);
        BackupCreateRequest request = Request(root, database, key);

        BackupCreateResult first = await service.CreateAsync(request);
        BackupCreateResult second = await service.CreateAsync(request);

        Assert.True(first.Succeeded); Assert.True(second.Succeeded);
        Assert.Equal("MWPV_Backup_2026-07-10_143522", first.Backup!.Manifest.PhysicalFolderName);
        Assert.Equal("MWPV_Backup_2026-07-10_143522_01", second.Backup!.Manifest.PhysicalFolderName);
        Assert.NotEqual(first.Backup.Manifest.BackupSetId, first.Backup.Manifest.PhysicalFolderName);
        Assert.All(first.Backup.Manifest.Files, file => Assert.False(Path.IsPathRooted(file.DestinationRelativePath)));
        Assert.DoesNotContain(database, File.ReadAllText(Path.Combine(first.Backup.BackupFolder, "manifest.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsTraversalAndDuplicateRoles()
    {
        using var temp = new TempDirectory(); string source = Path.Combine(temp.PathValue, "a.db"); File.WriteAllText(source, "a");
        var service = new BackupService();
        var result = await service.CreateAsync(new BackupCreateRequest
        {
            BackupRoot = temp.Create("backups"), BackupType = BackupTypes.Exit, ApplicationName = "MWPV",
            Files =
            [
                new() { Role = "Database", SourcePath = source, DestinationRelativePath = "../escape", Required = true },
                new() { Role = "Database", SourcePath = source, DestinationRelativePath = "files/a", Required = true }
            ]
        });
        Assert.Equal(BackupOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_DetectsTampering()
    {
        using var temp = new TempDirectory(); string sources = temp.Create("sources"); string root = temp.Create("backups");
        string database = Path.Combine(sources, "MWPV.db"); string key = Path.Combine(sources, "vault.pv");
        File.WriteAllText(database, "database"); File.WriteAllText(key, "key");
        var service = new BackupService(); BackupCreateResult created = await service.CreateAsync(Request(root, database, key));
        File.AppendAllText(Path.Combine(created.Backup!.BackupFolder, "files", "MWPV.db"), "tampered");
        BackupVerifyResult verified = await service.VerifyAsync(created.Backup.BackupFolder);
        Assert.Equal(BackupOperationStatus.VerificationFailed, verified.Status);
    }

    [Fact]
    public async Task Retention_UsesVerifiedManifestsAndProtectsNewest()
    {
        using var temp = new TempDirectory(); string sources = temp.Create("sources"); string root = temp.Create("backups");
        string database = Path.Combine(sources, "MWPV.db"); string key = Path.Combine(sources, "vault.pv");
        File.WriteAllText(database, "database"); File.WriteAllText(key, "key");
        DateTimeOffset now = new(2026, 7, 10, 14, 35, 20, TimeSpan.Zero);
        var service1 = new BackupService(() => now); var service2 = new BackupService(() => now.AddSeconds(1));
        BackupCreateResult old = await service1.CreateAsync(Request(root, database, key));
        BackupCreateResult newest = await service2.CreateAsync(Request(root, database, key));
        Directory.CreateDirectory(Path.Combine(root, "MWPV_Backup_2026-07-10_000000"));
        BackupRetentionResult result = await service2.ApplyRetentionAsync(new BackupRetentionRequest
        {
            BackupRoot = root, BackupType = BackupTypes.Exit, RetainCount = 1,
            ProtectedBackupSetId = newest.Backup!.Manifest.BackupSetId
        });
        Assert.True(result.Succeeded); Assert.False(Directory.Exists(old.Backup!.BackupFolder));
        Assert.True(Directory.Exists(newest.Backup.BackupFolder));
        Assert.True(Directory.Exists(Path.Combine(root, "MWPV_Backup_2026-07-10_000000")));
    }

    [Fact]
    public async Task RestoreAsync_UsesCallerDestinationsAndRemovesAbsentSidecar()
    {
        using var temp = new TempDirectory(); string sources = temp.Create("sources"); string root = temp.Create("backups");
        string database = Path.Combine(sources, "MWPV.db"); string key = Path.Combine(sources, "vault.pv");
        File.WriteAllText(database, "original-db"); File.WriteAllText(key, "original-key");
        var service = new BackupService(); BackupCreateResult created = await service.CreateAsync(Request(root, database, key));
        string targets = temp.Create("targets"); string dbTarget = Path.Combine(targets, "current.db"); string keyTarget = Path.Combine(targets, "current.pv");
        string walTarget = dbTarget + "-wal"; File.WriteAllText(dbTarget, "changed"); File.WriteAllText(keyTarget, "changed"); File.WriteAllText(walTarget, "stale");
        BackupRestoreResult restored = await service.RestoreAsync(new BackupRestoreRequest
        {
            BackupFolder = created.Backup!.BackupFolder, ExpectedBackupType = BackupTypes.Exit,
            Destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PasswordDatabase"] = dbTarget, ["PasswordDatabaseWal"] = walTarget,
                ["KeyFileDatabase"] = keyTarget
            },
            RemoveTargetsForAbsentOptionalFiles = true
        });
        Assert.True(restored.Succeeded); Assert.Equal("original-db", File.ReadAllText(dbTarget));
        Assert.Equal("original-key", File.ReadAllText(keyTarget)); Assert.False(File.Exists(walTarget));
    }

    [Fact]
    public async Task LegacyGuidManifest_IsReadOnlyVerifiedAndRestoredToCallerMapping()
    {
        using var temp = new TempDirectory(); string root = temp.Create("upgrade-backups");
        string id = Guid.NewGuid().ToString("D"); string folder = Path.Combine(root, id); string files = Path.Combine(folder, "files");
        Directory.CreateDirectory(files); string backupFile = Path.Combine(files, "PasswordDatabase.db"); File.WriteAllText(backupFile, "legacy");
        string hash; using (var stream = File.OpenRead(backupFile)) hash = Convert.ToHexString(SHA256.HashData(stream));
        string manifestPath = Path.Combine(folder, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            BackupSetId = id, BackupRoot = folder, ManifestPath = Path.Combine(folder, "manifest.json"),
            CreatedUtc = DateTimeOffset.UtcNow, AppVersion = "1.0.0",
            Files = new[] { new { Role = "PasswordDatabase", OriginalPath = "C:\\sensitive\\MWPV.db", BackupPath = backupFile,
                Required = true, WasPresent = true, Size = new FileInfo(backupFile).Length, Sha256 = hash } }
        }));
        string originalManifest = File.ReadAllText(manifestPath);
        var service = new BackupService(); BackupVerifyResult verified = await service.VerifyAsync(folder);
        Assert.True(verified.Succeeded); Assert.True(verified.Backup!.IsLegacy);
        string target = Path.Combine(temp.Create("targets"), "MWPV.db");
        BackupRestoreResult restored = await service.RestoreAsync(new BackupRestoreRequest
        {
            BackupFolder = folder, ExpectedBackupType = BackupTypes.Upgrade,
            Destinations = new Dictionary<string, string> { ["PasswordDatabase"] = target }
        });
        Assert.True(restored.Succeeded); Assert.Equal("legacy", File.ReadAllText(target));
        Assert.Equal(originalManifest, File.ReadAllText(manifestPath));
    }

    private static BackupCreateRequest Request(string root, string database, string key) => new()
    {
        BackupRoot = root, FolderPrefix = "MWPV_Backup", BackupType = BackupTypes.Exit,
        ApplicationName = "MWPV", ApplicationVersion = "1.0.0",
        Files =
        [
            new() { Role = "PasswordDatabase", SourcePath = database, DestinationRelativePath = "files/MWPV.db", Required = true },
            new() { Role = "PasswordDatabaseWal", SourcePath = database + "-wal", DestinationRelativePath = "files/PasswordDatabase.wal", Required = false },
            new() { Role = "KeyFileDatabase", SourcePath = key, DestinationRelativePath = "files/KeyFileDatabase.pv", Required = true }
        ]
    };
}
