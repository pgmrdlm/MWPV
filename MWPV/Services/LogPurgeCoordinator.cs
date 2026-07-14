using System.Globalization;
using System.IO;
using System.Reflection;
using Backup.Utility;
using Microsoft.Data.Sqlite;
using Security.Utility.Storage;
using Utilities.Helpers;
using Utilities.Sql;

namespace MWPV.Services;

public sealed class LogPurgeCoordinator
{
    private const string KeyFileSetting = "KeyFile";
    private readonly IBackupService _backups;

    public LogPurgeCoordinator() : this(new BackupService()) { }
    internal LogPurgeCoordinator(IBackupService backups) => _backups = backups;

    public sealed record Preview(int RetentionDays, DateTimeOffset CutoffUtc, long Total, long Starts, long Ends,
        string? OldestWhenUtc, string? NewestWhenUtc);
    public sealed record Result(bool Succeeded, Preview Deleted, string? BackupSetId = null);

    public Task<Preview> GetPreviewAsync() => Task.Run(GetPreview);

    private static Preview GetPreview()
    {
        int retention = AppSettingsService.LoadEditableSettings().LogRetentionDays;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention);
        using var connection = DatabaseHelper.GetAppOpenConnection();
        return ReadPreview(connection, null, retention, cutoff);
    }

    public async Task<Result> PurgeAsync(DateTimeOffset purgeStartedUtc, Action<string>? stage = null)
    {
        int retention = AppSettingsService.LoadEditableSettings().LogRetentionDays;
        var cutoff = purgeStartedUtc.AddDays(-retention);
        stage?.Invoke("Creating and verifying backup before log purge...");
        if (!RunFullCheckpoint()) return new Result(false, EmptyPreview(retention, cutoff));
        SqliteConnection.ClearAllPools();

        string dbPath = DatabaseHelper.GetAppDbPath();
        string keyFile = SecureEncryptedDataStore.GetString(KeyFileSetting);
        string root = Path.Combine(Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory, "log-purge-backups");
        var created = await _backups.CreateAsync(new BackupCreateRequest
        {
            BackupRoot = root, FolderPrefix = "LogPurge_Backup", BackupType = BackupTypes.Manual,
            ApplicationName = "MWPV", ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty,
            Files = BackupOnExitCoordinator.BuildVaultFiles(dbPath, keyFile)
        }).ConfigureAwait(false);
        if (!created.Succeeded || created.Backup == null) return new Result(false, EmptyPreview(retention, cutoff));

        var verified = await _backups.VerifyAsync(created.Backup.BackupFolder).ConfigureAwait(false);
        if (!verified.Succeeded || verified.Backup == null) return new Result(false, EmptyPreview(retention, cutoff));

        stage?.Invoke("Purging retained session logs...");
        try
        {
            using var connection = DatabaseHelper.GetAppOpenConnection();
            using (var timeout = connection.CreateCommand()) { timeout.CommandText = "PRAGMA busy_timeout = 10000;"; timeout.ExecuteNonQuery(); }
            using var transaction = connection.BeginTransaction(deferred: false);
            var preview = ReadPreview(connection, transaction, retention, cutoff);
            if (preview.Total == 0) { transaction.Rollback(); return new Result(true, preview, verified.Backup.Manifest.BackupSetId); }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = RequiredSql("s_Logs_PurgeSessionDelete.sql");
                delete.Parameters.AddWithValue("@CutoffUtc", cutoff.ToString("o", CultureInfo.InvariantCulture));
                if (delete.ExecuteNonQuery() != preview.Total) throw new InvalidOperationException("The session-log purge row count changed.");
            }
            InsertAudit(connection, transaction, purgeStartedUtc, DateTimeOffset.UtcNow, preview, verified.Backup.Manifest.BackupSetId);
            transaction.Commit();
            return new Result(true, preview, verified.Backup.Manifest.BackupSetId);
        }
        catch { return new Result(false, EmptyPreview(retention, cutoff)); }
    }

    private static Preview ReadPreview(SqliteConnection connection, SqliteTransaction? transaction, int retention, DateTimeOffset cutoff)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RequiredSql("s_Logs_PurgeSessionPreview.sql");
        command.Parameters.AddWithValue("@CutoffUtc", cutoff.ToString("o", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return EmptyPreview(retention, cutoff);
        return new Preview(retention, cutoff, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static void InsertAudit(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset started, DateTimeOffset completed, Preview deleted, string backupSetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RequiredSql("s_Logs_Insert.sql");
        string now = completed.ToString("o", CultureInfo.InvariantCulture);
        command.Parameters.AddWithValue("@WhenUtc", now); command.Parameters.AddWithValue("@CreatedUtc", now);
        command.Parameters.AddWithValue("@Level", "INFO"); command.Parameters.AddWithValue("@Source", "Logs"); command.Parameters.AddWithValue("@EventCode", "LOG_PURGE_COMPLETED");
        command.Parameters.AddWithValue("@SessionId", ""); command.Parameters.AddWithValue("@LoginId", DBNull.Value); command.Parameters.AddWithValue("@ItemId", DBNull.Value);
        command.Parameters.AddWithValue("@SubjectText", "Session log purge completed");
        command.Parameters.AddWithValue("@MessageText", $"PurgeStartedUtc={started:o}; PurgeCompletedUtc={completed:o}; RetentionDays={deleted.RetentionDays}; CutoffUtc={deleted.CutoffUtc:o}; OldestDeletedUtc={deleted.OldestWhenUtc ?? ""}; NewestDeletedUtc={deleted.NewestWhenUtc ?? ""}; SessionStartDeleted={deleted.Starts}; SessionEndDeleted={deleted.Ends}; TotalDeleted={deleted.Total}; BackupSetId={backupSetId}.");
        command.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? ""); command.Parameters.AddWithValue("@DeviceMake", DBNull.Value); command.Parameters.AddWithValue("@DeviceModel", DBNull.Value); command.Parameters.AddWithValue("@OSVersion", DBNull.Value); command.Parameters.AddWithValue("@DeviceIdHash", DBNull.Value); command.Parameters.AddWithValue("@InstallType", DBNull.Value);
        command.Parameters.AddWithValue("@AppVersion", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "dev"); command.Parameters.AddWithValue("@IsCrash", 0); command.Parameters.AddWithValue("@StackHash", ""); command.Parameters.AddWithValue("@KeySetVersion", 1);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The purge audit log could not be written.");
    }

    private static bool RunFullCheckpoint()
    {
        try { using var c = DatabaseHelper.GetAppOpenConnection(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA wal_checkpoint(FULL);"; using var r = cmd.ExecuteReader(); return r.Read() && Convert.ToInt32(r.GetValue(0)) == 0 && Convert.ToInt32(r.GetValue(2)) >= Convert.ToInt32(r.GetValue(1)); }
        catch { return false; }
    }
    private static string RequiredSql(string name) => RuntimeSqlStore.GetSql(name) is { Length: > 0 } sql ? sql : throw new InvalidOperationException($"SQL not loaded: {name}");
    private static Preview EmptyPreview(int retention, DateTimeOffset cutoff) => new(retention, cutoff, 0, 0, 0, null, null);
}
