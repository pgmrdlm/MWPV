INSERT INTO KeyArchiveIntegrity (kai_Id, kai_ArchiveSha256, kai_ArchiveSize, kai_WrittenUtc)
VALUES (1, @sha, @size, STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'))
ON CONFLICT(kai_Id) DO UPDATE SET
    kai_ArchiveSha256 = excluded.kai_ArchiveSha256,
    kai_ArchiveSize   = excluded.kai_ArchiveSize,
    kai_WrittenUtc    = STRFTIME('%Y-%m-%dT%H:%M:%fZ','now');
