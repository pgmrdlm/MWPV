using System.Security.Cryptography;
using System.Text.Json;

namespace Backup.Utility;

public sealed class BackupService : IBackupService
{
    private const int ManifestVersion = 1;
    private const string ManifestFileName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly Func<DateTimeOffset> _now;

    public BackupService() : this(() => DateTimeOffset.Now) { }
    internal BackupService(Func<DateTimeOffset> now) => _now = now;

    public Task<BackupCreateResult> CreateAsync(BackupCreateRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(request, cancellationToken), CancellationToken.None);

    public Task<BackupVerifyResult> VerifyAsync(string backupFolder, CancellationToken cancellationToken = default) =>
        Task.Run(() => Verify(backupFolder, cancellationToken), CancellationToken.None);

    public Task<BackupLoadResult> LoadAsync(string backupFolder, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(backupFolder, cancellationToken), CancellationToken.None);

    public Task<BackupRetentionResult> ApplyRetentionAsync(BackupRetentionRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplyRetention(request, cancellationToken), CancellationToken.None);

    public Task<BackupRestoreResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Restore(request, cancellationToken), CancellationToken.None);

    private BackupCreateResult Create(BackupCreateRequest request, CancellationToken token)
    {
        string staging = string.Empty;
        try
        {
            token.ThrowIfCancellationRequested();
            string? validation = ValidateCreateRequest(request, out string root);
            if (validation != null) return CreateFailure(BackupOperationStatus.InvalidRequest, validation);
            Directory.CreateDirectory(root);
            if (IsReparsePoint(root)) return CreateFailure(BackupOperationStatus.InvalidRequest, "The backup root cannot be a reparse point.");

            DateTimeOffset now = _now();
            string folderName = AllocateFolderName(root, request.FolderPrefix, now.LocalDateTime);
            string final = Path.Combine(root, folderName);
            staging = Path.Combine(root, $".{folderName}.incomplete-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);

            var files = new List<BackupManifestFile>();
            foreach (BackupSourceFile source in request.Files)
            {
                token.ThrowIfCancellationRequested();
                string fullSource = Path.GetFullPath(source.SourcePath);
                bool present = File.Exists(fullSource);
                if (source.Required && !present)
                    return CleanupCreateFailure(staging, BackupOperationStatus.NotFound, $"A required source file for role {source.Role} was not found.");

                if (!present)
                {
                    files.Add(new BackupManifestFile
                    {
                        Role = source.Role.Trim(), Required = false, WasPresent = false,
                        SourceIdentity = Path.GetFileName(fullSource), DestinationRelativePath = NormalizeRelative(source.DestinationRelativePath)
                    });
                    continue;
                }

                string relative = NormalizeRelative(source.DestinationRelativePath);
                string destination = ResolveContainedPath(staging, relative);
                EnsureNoReparseAncestors(staging, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(fullSource, destination, false);
                var info = new FileInfo(destination);
                files.Add(new BackupManifestFile
                {
                    Role = source.Role.Trim(), Required = source.Required, WasPresent = true,
                    SourceIdentity = Path.GetFileName(fullSource), DestinationRelativePath = relative,
                    Size = info.Length, Sha256 = ComputeSha256(destination)
                });
            }

            var manifest = new BackupManifest
            {
                ManifestVersion = ManifestVersion,
                BackupSetId = Guid.NewGuid().ToString("D"),
                BackupType = request.BackupType,
                CreatedUtc = now.ToUniversalTime(),
                ApplicationName = request.ApplicationName.Trim(),
                ApplicationVersion = request.ApplicationVersion.Trim(),
                PhysicalFolderName = folderName,
                VerificationAlgorithm = "SHA-256",
                Files = files
            };
            WriteManifest(staging, manifest);
            var staged = VerifyNew(staging, folderName, requireActualFolderName: false, token);
            if (!staged.Succeeded)
                return CleanupCreateFailure(staging, BackupOperationStatus.VerificationFailed, staged.SafeMessage);

            manifest = manifest with { VerifiedUtc = DateTimeOffset.UtcNow, VerificationSucceeded = true };
            WriteManifest(staging, manifest);
            if (Directory.Exists(final) || File.Exists(final))
                return CleanupCreateFailure(staging, BackupOperationStatus.DestinationCollision, "The allocated backup destination already exists.");
            Directory.Move(staging, final);
            staging = string.Empty;

            var verified = VerifyNew(final, folderName, requireActualFolderName: true, token);
            if (!verified.Succeeded)
                return CreateFailure(BackupOperationStatus.VerificationFailed, verified.SafeMessage);
            return new BackupCreateResult { Status = BackupOperationStatus.Success, SafeMessage = "The backup was created and verified.", Backup = verified.Backup };
        }
        catch (OperationCanceledException) { return CleanupCreateFailure(staging, BackupOperationStatus.Canceled, "The backup was canceled."); }
        catch { return CleanupCreateFailure(staging, BackupOperationStatus.Failed, "The backup operation failed."); }
    }

    private BackupVerifyResult Verify(string backupFolder, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            string folder = NormalizeExistingFolder(backupFolder);
            return File.Exists(Path.Combine(folder, ManifestFileName)) && IsLegacyManifest(folder)
                ? VerifyLegacy(folder, token)
                : VerifyNew(folder, Path.GetFileName(folder), true, token);
        }
        catch (OperationCanceledException) { return VerifyFailure(BackupOperationStatus.Canceled, "Backup verification was canceled."); }
        catch (DirectoryNotFoundException) { return VerifyFailure(BackupOperationStatus.NotFound, "The backup folder was not found."); }
        catch { return VerifyFailure(BackupOperationStatus.VerificationFailed, "Backup verification failed."); }
    }

    private BackupLoadResult Load(string backupFolder, CancellationToken token)
    {
        BackupVerifyResult verified = Verify(backupFolder, token);
        return new BackupLoadResult { Status = verified.Status, SafeMessage = verified.SafeMessage, Backup = verified.Backup };
    }

    private BackupRetentionResult ApplyRetention(BackupRetentionRequest request, CancellationToken token)
    {
        var retained = new List<string>(); var deleted = new List<string>(); var skipped = new List<string>(); var failed = new List<string>();
        try
        {
            if (request.RetainCount < 1 || string.IsNullOrWhiteSpace(request.BackupRoot) || !Path.IsPathRooted(request.BackupRoot) ||
                (request.BackupType != null && !BackupTypes.IsSupported(request.BackupType)))
                return RetentionFailure(BackupOperationStatus.InvalidRequest, "The retention request is invalid.", retained, deleted, skipped, failed);
            string root = NormalizeExistingFolder(request.BackupRoot);
            var eligible = new List<BackupDescriptor>();
            foreach (string child in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(child);
                if (name.StartsWith(".", StringComparison.Ordinal) || IsReparsePoint(child) || !IsImmediateChild(root, child)) { skipped.Add(name); continue; }
                BackupVerifyResult verification = Verify(child, token);
                if (!verification.Succeeded || verification.Backup == null ||
                    (verification.Backup.IsLegacy && !request.IncludeVerifiedLegacyBackups) ||
                    (request.BackupType != null && !string.Equals(verification.Backup.Manifest.BackupType, request.BackupType, StringComparison.Ordinal)))
                { skipped.Add(name); continue; }
                eligible.Add(verification.Backup);
            }
            var ordered = eligible.OrderByDescending(x => string.Equals(x.Manifest.BackupSetId, request.ProtectedBackupSetId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Manifest.CreatedUtc).ThenByDescending(x => x.Manifest.PhysicalFolderName, StringComparer.OrdinalIgnoreCase).ToArray();
            retained.AddRange(ordered.Take(request.RetainCount).Select(x => x.Manifest.PhysicalFolderName));
            foreach (BackupDescriptor item in ordered.Skip(request.RetainCount))
            {
                if (string.Equals(item.Manifest.BackupSetId, request.ProtectedBackupSetId, StringComparison.OrdinalIgnoreCase)) { retained.Add(item.Manifest.PhysicalFolderName); continue; }
                try
                {
                    if (!IsImmediateChild(root, item.BackupFolder) || TreeContainsReparsePoint(item.BackupFolder)) { skipped.Add(item.Manifest.PhysicalFolderName); continue; }
                    Directory.Delete(item.BackupFolder, true); deleted.Add(item.Manifest.PhysicalFolderName);
                }
                catch { failed.Add(item.Manifest.PhysicalFolderName); }
            }
            return new BackupRetentionResult { Status = failed.Count == 0 ? BackupOperationStatus.Success : BackupOperationStatus.PartialFailure,
                SafeMessage = failed.Count == 0 ? "Backup retention completed." : "Some backup folders could not be deleted.", RetainedFolders = retained, DeletedFolders = deleted, SkippedFolders = skipped, FailedFolders = failed };
        }
        catch (OperationCanceledException) { return RetentionFailure(BackupOperationStatus.Canceled, "Backup retention was canceled.", retained, deleted, skipped, failed); }
        catch { return RetentionFailure(BackupOperationStatus.Failed, "Backup retention failed.", retained, deleted, skipped, failed); }
    }

    private BackupRestoreResult Restore(BackupRestoreRequest request, CancellationToken token)
    {
        var results = new List<BackupRestoreFileResult>();
        try
        {
            if (!BackupTypes.IsSupported(request.ExpectedBackupType) || request.Destinations.Count == 0)
                return RestoreFailure(BackupOperationStatus.InvalidRequest, "The restore request is invalid.", results);
            BackupVerifyResult verified = Verify(request.BackupFolder, token);
            if (!verified.Succeeded || verified.Backup == null)
                return RestoreFailure(BackupOperationStatus.VerificationFailed, verified.SafeMessage, results);
            if (!string.Equals(verified.Backup.Manifest.BackupType, request.ExpectedBackupType, StringComparison.Ordinal))
                return RestoreFailure(BackupOperationStatus.InvalidRequest, "The backup type does not match the restore request.", results);

            string folder = verified.Backup.BackupFolder;
            foreach (BackupManifestFile entry in verified.Backup.Manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                if (!request.Destinations.TryGetValue(entry.Role, out string? destination) || string.IsNullOrWhiteSpace(destination) || !Path.IsPathRooted(destination))
                {
                    if (entry.Required) return RestoreFailure(BackupOperationStatus.InvalidRequest, $"A restore destination is required for role {entry.Role}.", results);
                    continue;
                }
                string target = Path.GetFullPath(destination);
                if (!entry.WasPresent)
                {
                    if (request.RemoveTargetsForAbsentOptionalFiles && File.Exists(target)) { ClearReadOnly(target); File.Delete(target); }
                    results.Add(new() { Role = entry.Role, Succeeded = true, SafeMessage = "The optional absent-file state was restored." });
                    continue;
                }
                string source = ResolveManifestFilePath(folder, entry, verified.Backup.IsLegacy);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + $".restore-{Guid.NewGuid():N}.tmp";
                try
                {
                    File.Copy(source, temporary, false);
                    if (new FileInfo(temporary).Length != entry.Size || !string.Equals(ComputeSha256(temporary), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException();
                    ClearReadOnly(target); File.Move(temporary, target, true);
                    if (!string.Equals(ComputeSha256(target), entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                    results.Add(new() { Role = entry.Role, Succeeded = true, SafeMessage = "The file was restored and verified." });
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return new BackupRestoreResult { Status = BackupOperationStatus.Success, SafeMessage = "The backup was restored and verified.", Files = results };
        }
        catch (OperationCanceledException) { return RestoreFailure(BackupOperationStatus.Canceled, "The restore was canceled.", results); }
        catch { return RestoreFailure(BackupOperationStatus.Failed, "The backup restore failed.", results); }
    }

    private static BackupVerifyResult VerifyNew(string folder, string expectedFolderName, bool requireActualFolderName, CancellationToken token)
    {
        try
        {
            if (IsReparsePoint(folder)) return VerifyFailure(BackupOperationStatus.VerificationFailed, "The backup folder is a reparse point.");
            string manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath)) return VerifyFailure(BackupOperationStatus.InvalidManifest, "The backup manifest is missing.");
            BackupManifest? manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest == null || manifest.ManifestVersion != ManifestVersion || !Guid.TryParse(manifest.BackupSetId, out _) ||
                !BackupTypes.IsSupported(manifest.BackupType) || !string.Equals(manifest.VerificationAlgorithm, "SHA-256", StringComparison.Ordinal) ||
                !string.Equals(manifest.PhysicalFolderName, expectedFolderName, StringComparison.Ordinal) ||
                (requireActualFolderName && !string.Equals(manifest.PhysicalFolderName, Path.GetFileName(folder), StringComparison.Ordinal)))
                return VerifyFailure(BackupOperationStatus.InvalidManifest, "The backup manifest is invalid or inconsistent with its folder.");
            string? structure = ValidateManifestFiles(manifest.Files);
            if (structure != null) return VerifyFailure(BackupOperationStatus.InvalidManifest, structure);
            foreach (BackupManifestFile entry in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                string path = ResolveContainedPath(folder, entry.DestinationRelativePath);
                EnsureNoReparseAncestors(folder, path);
                if (!entry.WasPresent)
                {
                    if (entry.Required || File.Exists(path)) return VerifyFailure(BackupOperationStatus.VerificationFailed, $"The absent-file state for role {entry.Role} is invalid.");
                    continue;
                }
                if (!File.Exists(path) || entry.Size == null || string.IsNullOrWhiteSpace(entry.Sha256) || new FileInfo(path).Length != entry.Size ||
                    !string.Equals(ComputeSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    return VerifyFailure(BackupOperationStatus.VerificationFailed, $"Backup verification failed for role {entry.Role}.");
            }
            return new BackupVerifyResult { Status = BackupOperationStatus.Success, SafeMessage = "The backup was verified.",
                Backup = new BackupDescriptor { BackupFolder = Path.GetFullPath(folder), Manifest = manifest } };
        }
        catch { return VerifyFailure(BackupOperationStatus.VerificationFailed, "Backup verification failed."); }
    }

    private static BackupVerifyResult VerifyLegacy(string folder, CancellationToken token)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ManifestFileName)));
            JsonElement root = document.RootElement;
            string id = GetString(root, "BackupSetId");
            DateTimeOffset created = root.TryGetProperty("CreatedUtc", out JsonElement createdElement) && createdElement.TryGetDateTimeOffset(out var parsed) ? parsed : default;
            if (!Guid.TryParse(id, out _) || !root.TryGetProperty("Files", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
                return VerifyFailure(BackupOperationStatus.InvalidManifest, "The legacy backup manifest is invalid.");
            var entries = new List<BackupManifestFile>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                string role = GetString(item, "Role"); string backupPath = GetString(item, "BackupPath");
                bool required = GetBool(item, "Required"); bool present = GetBool(item, "WasPresent");
                string relative = present ? Path.GetRelativePath(folder, Path.GetFullPath(backupPath)) : $"legacy-absent/{role}";
                if (present && (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)))
                    return VerifyFailure(BackupOperationStatus.VerificationFailed, "A legacy backup file is outside its backup folder.");
                long? size = item.TryGetProperty("Size", out JsonElement sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : null;
                string? hash = item.TryGetProperty("Sha256", out JsonElement hashElement) ? hashElement.GetString() : null;
                var entry = new BackupManifestFile { Role = role, Required = required, WasPresent = present, SourceIdentity = role,
                    DestinationRelativePath = NormalizeRelative(relative), Size = size, Sha256 = hash };
                if (present)
                {
                    string contained = ResolveContainedPath(folder, entry.DestinationRelativePath); EnsureNoReparseAncestors(folder, contained);
                    if (!File.Exists(contained) || size == null || string.IsNullOrWhiteSpace(hash) || new FileInfo(contained).Length != size ||
                        !string.Equals(ComputeSha256(contained), hash, StringComparison.OrdinalIgnoreCase))
                        return VerifyFailure(BackupOperationStatus.VerificationFailed, $"Legacy backup verification failed for role {role}.");
                }
                else if (required) return VerifyFailure(BackupOperationStatus.VerificationFailed, $"A required legacy entry is absent: {role}.");
                entries.Add(entry);
            }
            string? structure = ValidateManifestFiles(entries); if (structure != null) return VerifyFailure(BackupOperationStatus.InvalidManifest, structure);
            var manifest = new BackupManifest { ManifestVersion = 0, BackupSetId = id, BackupType = BackupTypes.Upgrade, CreatedUtc = created,
                ApplicationName = "MWPV", ApplicationVersion = GetString(root, "AppVersion"), PhysicalFolderName = Path.GetFileName(folder),
                VerificationAlgorithm = "SHA-256", VerificationSucceeded = true, Files = entries };
            return new BackupVerifyResult { Status = BackupOperationStatus.Success, SafeMessage = "The legacy backup was verified.",
                Backup = new BackupDescriptor { BackupFolder = folder, IsLegacy = true, Manifest = manifest } };
        }
        catch { return VerifyFailure(BackupOperationStatus.VerificationFailed, "Legacy backup verification failed."); }
    }

    private static string? ValidateCreateRequest(BackupCreateRequest request, out string root)
    {
        root = string.Empty;
        if (request == null || string.IsNullOrWhiteSpace(request.BackupRoot) || !Path.IsPathRooted(request.BackupRoot) ||
            string.IsNullOrWhiteSpace(request.FolderPrefix) || request.FolderPrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !BackupTypes.IsSupported(request.BackupType) || string.IsNullOrWhiteSpace(request.ApplicationName) || request.Files.Count == 0)
            return "The backup request is invalid.";
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.BackupRoot));
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BackupSourceFile file in request.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Role) || string.IsNullOrWhiteSpace(file.SourcePath) || !Path.IsPathRooted(file.SourcePath) ||
                !roles.Add(file.Role.Trim()) || !TryNormalizeRelative(file.DestinationRelativePath, out string relative) || !paths.Add(relative))
                return "Backup roles and destination-relative paths must be valid and unique.";
        }
        return null;
    }

    private static string? ValidateManifestFiles(IReadOnlyList<BackupManifestFile>? files)
    {
        if (files == null || files.Count == 0) return "The backup manifest contains no files.";
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            if (string.IsNullOrWhiteSpace(file.Role) || !roles.Add(file.Role) || !TryNormalizeRelative(file.DestinationRelativePath, out string relative) || !paths.Add(relative))
                return "The backup manifest contains invalid or duplicate file entries.";
        return null;
    }

    private static string AllocateFolderName(string root, string prefix, DateTime local)
    {
        string basis = $"{prefix}_{local:yyyy-MM-dd_HHmmss}";
        for (int suffix = 0; suffix <= 99; suffix++)
        {
            string candidate = suffix == 0 ? basis : $"{basis}_{suffix:00}";
            if (!Directory.Exists(Path.Combine(root, candidate)) && !File.Exists(Path.Combine(root, candidate))) return candidate;
        }
        throw new IOException("No backup folder name is available for the current timestamp.");
    }

    private static void WriteManifest(string folder, BackupManifest manifest) =>
        File.WriteAllText(Path.Combine(folder, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions));

    private static bool IsLegacyManifest(string folder)
    {
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ManifestFileName))); return !doc.RootElement.TryGetProperty("ManifestVersion", out _); }
        catch { return false; }
    }

    private static string NormalizeExistingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new DirectoryNotFoundException();
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException();
        return full;
    }

    private static bool TryNormalizeRelative(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return false;
        string candidate = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (candidate.Split(Path.DirectorySeparatorChar).Any(part => part is "" or "." or "..")) return false;
        normalized = candidate; return true;
    }

    private static string NormalizeRelative(string path) => TryNormalizeRelative(path, out string normalized) ? normalized : throw new InvalidDataException();
    private static string ResolveContainedPath(string root, string relative)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, NormalizeRelative(relative)));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        return candidate;
    }
    private static string ResolveManifestFilePath(string folder, BackupManifestFile file, bool legacy) => ResolveContainedPath(folder, file.DestinationRelativePath);
    private static bool IsImmediateChild(string root, string child) => string.Equals(Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(child)))?.FullName,
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase);
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void EnsureNoReparseAncestors(string root, string target)
    {
        string current = Path.GetDirectoryName(target)!; string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (current.Length >= normalizedRoot.Length)
        {
            if (Directory.Exists(current) && IsReparsePoint(current)) throw new InvalidDataException();
            if (string.Equals(Path.TrimEndingDirectorySeparator(current), normalizedRoot, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? throw new InvalidDataException();
        }
    }
    private static bool TreeContainsReparsePoint(string root)
    {
        if (IsReparsePoint(root)) return true;
        foreach (string item in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)) if (IsReparsePoint(item)) return true;
        return false;
    }
    private static string ComputeSha256(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void ClearReadOnly(string path) { if (!File.Exists(path)) return; var a = File.GetAttributes(path); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, a & ~FileAttributes.ReadOnly); }
    private static string GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    private static bool GetBool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static BackupCreateResult CreateFailure(BackupOperationStatus status, string message) => new() { Status = status, SafeMessage = message };
    private static BackupCreateResult CleanupCreateFailure(string staging, BackupOperationStatus status, string message)
    {
        bool cleaned = true; if (!string.IsNullOrWhiteSpace(staging) && Directory.Exists(staging)) try { Directory.Delete(staging, true); } catch { cleaned = false; }
        return new() { Status = cleaned ? status : BackupOperationStatus.CleanupFailed, SafeMessage = cleaned ? message : "The backup failed and its incomplete folder could not be removed.", CleanupSucceeded = cleaned };
    }
    private static BackupVerifyResult VerifyFailure(BackupOperationStatus status, string message) => new() { Status = status, SafeMessage = message };
    private static BackupRetentionResult RetentionFailure(BackupOperationStatus status, string message, List<string> retained, List<string> deleted, List<string> skipped, List<string> failed) =>
        new() { Status = status, SafeMessage = message, RetainedFolders = retained, DeletedFolders = deleted, SkippedFolders = skipped, FailedFolders = failed };
    private static BackupRestoreResult RestoreFailure(BackupOperationStatus status, string message, List<BackupRestoreFileResult> files) => new() { Status = status, SafeMessage = message, Files = files };
}
