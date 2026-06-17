namespace MWPV.Services.Upgrade
{
    public sealed record BackupFileEntry
    {
        public string Role { get; init; } = string.Empty;
        public string OriginalPath { get; init; } = string.Empty;
        public string BackupPath { get; init; } = string.Empty;
        public bool Required { get; init; }
        public long? Size { get; init; }
        public string? Sha256 { get; init; }
    }
}
