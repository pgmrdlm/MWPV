namespace Backup.Utility;

public static class BackupTypes
{
    public const string Upgrade = "Upgrade";
    public const string Exit = "Exit";
    public const string Manual = "Manual";

    public static bool IsSupported(string? value) =>
        value is Upgrade or Exit or Manual;
}

public sealed record BackupSourceFile
{
    public string Role { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string DestinationRelativePath { get; init; } = string.Empty;
    public bool Required { get; init; }
}

public sealed record BackupCreateRequest
{
    public string BackupRoot { get; init; } = string.Empty;
    public string FolderPrefix { get; init; } = "MWPV_Backup";
    public string BackupType { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public IReadOnlyList<BackupSourceFile> Files { get; init; } = Array.Empty<BackupSourceFile>();
}

public sealed record BackupManifest
{
    public int ManifestVersion { get; init; } = 1;
    public string BackupSetId { get; init; } = string.Empty;
    public string BackupType { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string PhysicalFolderName { get; init; } = string.Empty;
    public string VerificationAlgorithm { get; init; } = "SHA-256";
    public DateTimeOffset? VerifiedUtc { get; init; }
    public bool VerificationSucceeded { get; init; }
    public IReadOnlyList<BackupManifestFile> Files { get; init; } = Array.Empty<BackupManifestFile>();
}

public sealed record BackupManifestFile
{
    public string Role { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool WasPresent { get; init; }
    public string SourceIdentity { get; init; } = string.Empty;
    public string DestinationRelativePath { get; init; } = string.Empty;
    public long? Size { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record BackupDescriptor
{
    public string BackupFolder { get; init; } = string.Empty;
    public bool IsLegacy { get; init; }
    public BackupManifest Manifest { get; init; } = new();
}

public enum BackupOperationStatus
{
    Success,
    SuccessWithWarnings,
    InvalidRequest,
    NotFound,
    InvalidManifest,
    VerificationFailed,
    DestinationCollision,
    Canceled,
    PartialFailure,
    Failed,
    CleanupFailed
}

public abstract record BackupOperationResult
{
    public BackupOperationStatus Status { get; init; }
    public string SafeMessage { get; init; } = string.Empty;
    public bool Succeeded => Status is BackupOperationStatus.Success or BackupOperationStatus.SuccessWithWarnings;
}

public sealed record BackupCreateResult : BackupOperationResult
{
    public BackupDescriptor? Backup { get; init; }
    public bool CleanupSucceeded { get; init; } = true;
}

public sealed record BackupVerifyResult : BackupOperationResult
{
    public BackupDescriptor? Backup { get; init; }
}

public sealed record BackupLoadResult : BackupOperationResult
{
    public BackupDescriptor? Backup { get; init; }
}

public sealed record BackupRetentionRequest
{
    public string BackupRoot { get; init; } = string.Empty;
    public int RetainCount { get; init; }
    public string? BackupType { get; init; }
    public string? ProtectedBackupSetId { get; init; }
    public bool IncludeVerifiedLegacyBackups { get; init; }
}

public sealed record BackupRetentionResult : BackupOperationResult
{
    public IReadOnlyList<string> RetainedFolders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DeletedFolders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SkippedFolders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FailedFolders { get; init; } = Array.Empty<string>();
}

public sealed record BackupRestoreRequest
{
    public string BackupFolder { get; init; } = string.Empty;
    public string ExpectedBackupType { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Destinations { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool RemoveTargetsForAbsentOptionalFiles { get; init; }
}

public sealed record BackupRestoreFileResult
{
    public string Role { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public string SafeMessage { get; init; } = string.Empty;
}

public sealed record BackupRestoreResult : BackupOperationResult
{
    public IReadOnlyList<BackupRestoreFileResult> Files { get; init; } = Array.Empty<BackupRestoreFileResult>();
}
