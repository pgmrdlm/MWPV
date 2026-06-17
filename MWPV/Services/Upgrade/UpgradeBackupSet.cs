using System;
using System.Collections.Generic;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeBackupSet
    {
        public string BackupSetId { get; init; } = Guid.NewGuid().ToString("D");
        public string BackupRoot { get; init; } = string.Empty;
        public string ManifestPath { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
        public IReadOnlyList<BackupFileEntry> Files { get; init; } = Array.Empty<BackupFileEntry>();
    }
}
