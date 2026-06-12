using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MWPV.Services.MigrationUpgrade
{
    public sealed class MigrationUpgradeScript
    {
        public string FileName { get; init; } = string.Empty;
        public string? FromVersion { get; init; }
        public string? ToVersion { get; init; }
    }

    public sealed class MigrationUpgradePlan
    {
        public string CurrentDbVersion { get; init; } = string.Empty;
        public string TargetDbVersion { get; init; } = string.Empty;
        public IReadOnlyList<MigrationUpgradeScript> RequiredScripts { get; init; } = Array.Empty<MigrationUpgradeScript>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
        public bool IsNoOp { get; init; }
        public bool IsValid => Errors.Count == 0;
        public bool HasErrors => Errors.Count > 0;
    }

    public static class MigrationUpgradePlanner
    {
        private static readonly Regex UpgradeFileNameRegex = new(
            @"^v?(?<from>\d+(?:\.\d+)*)_v?(?<to>\d+(?:\.\d+)*)_Upgrade\.sql$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static MigrationUpgradePlan BuildPlan(
            string currentDbVersion,
            IReadOnlyList<MigrationUpgradeScript> availableScripts,
            string? targetDbVersion = null)
        {
            var warnings = new List<string>();
            var errors = new List<string>();
            var scripts = availableScripts?.ToArray() ?? Array.Empty<MigrationUpgradeScript>();
            var currentVersionText = currentDbVersion ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentVersionText))
                errors.Add("Current database version is required.");

            var currentKey = VersionKey.TryParse(currentVersionText, out var parsedCurrent)
                ? parsedCurrent
                : default;

            if (!string.IsNullOrWhiteSpace(currentVersionText) && currentKey.IsEmpty)
                errors.Add($"Current database version is malformed: {currentVersionText}");

            VersionKey? targetKey = null;
            string resolvedTargetVersion = targetDbVersion ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(targetDbVersion))
            {
                if (VersionKey.TryParse(targetDbVersion, out var parsedTarget) && !parsedTarget.IsEmpty)
                    targetKey = parsedTarget;
                else
                    errors.Add($"Target database version is malformed: {targetDbVersion}");

                if (errors.Count == 0 && currentKey.Equals(targetKey!.Value))
                {
                    return CreatePlan(
                        currentVersionText,
                        resolvedTargetVersion,
                        Array.Empty<MigrationUpgradeScript>(),
                        warnings,
                        errors,
                        isNoOp: true);
                }

                if (errors.Count == 0 && targetKey!.Value.CompareTo(currentKey) < 0)
                    errors.Add($"Target database version {targetDbVersion} is older than current database version {currentVersionText}.");
            }

            var edges = ParseEdges(scripts, errors);
            DetectDuplicateLinks(edges, errors);
            DetectAmbiguousBranches(edges, errors);

            if (string.IsNullOrWhiteSpace(targetDbVersion) && !currentKey.IsEmpty)
            {
                var highestReachable = FindHighestReachable(currentKey, edges);
                if (highestReachable != null)
                {
                    targetKey = highestReachable.To;
                    resolvedTargetVersion = highestReachable.ToVersion;
                }
                else
                {
                    resolvedTargetVersion = currentVersionText;
                    warnings.Add($"No higher reachable upgrade scripts found from {currentVersionText}.");
                    return CreatePlan(
                        currentVersionText,
                        resolvedTargetVersion,
                        Array.Empty<MigrationUpgradeScript>(),
                        warnings,
                        errors,
                        isNoOp: true);
                }
            }

            if (errors.Count > 0)
            {
                return CreatePlan(
                    currentVersionText,
                    resolvedTargetVersion,
                    Array.Empty<MigrationUpgradeScript>(),
                    warnings,
                    errors,
                    isNoOp: false);
            }

            if (targetKey == null)
            {
                errors.Add("Target database version could not be determined.");
                return CreatePlan(
                    currentVersionText,
                    resolvedTargetVersion,
                    Array.Empty<MigrationUpgradeScript>(),
                    warnings,
                    errors,
                    isNoOp: false);
            }

            if (currentKey.Equals(targetKey.Value))
            {
                return CreatePlan(
                    currentVersionText,
                    string.IsNullOrWhiteSpace(resolvedTargetVersion) ? currentVersionText : resolvedTargetVersion,
                    Array.Empty<MigrationUpgradeScript>(),
                    warnings,
                    errors,
                    isNoOp: true);
            }

            var required = BuildRequiredScriptList(currentKey, targetKey.Value, edges, errors);
            return CreatePlan(
                currentVersionText,
                string.IsNullOrWhiteSpace(resolvedTargetVersion) ? targetDbVersion ?? string.Empty : resolvedTargetVersion,
                required,
                warnings,
                errors,
                isNoOp: false);
        }

        private static IReadOnlyList<MigrationUpgradeScript> BuildRequiredScriptList(
            VersionKey current,
            VersionKey target,
            IReadOnlyList<UpgradeEdge> edges,
            List<string> errors)
        {
            var required = new List<MigrationUpgradeScript>();
            var cursor = current;
            var visited = new HashSet<VersionKey> { cursor };

            while (!cursor.Equals(target))
            {
                var next = edges
                    .Where(edge => edge.From.Equals(cursor) && edge.To.CompareTo(edge.From) > 0)
                    .OrderBy(edge => edge.To)
                    .ThenBy(edge => edge.Script.FileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(edge => edge.To.CompareTo(target) <= 0);

                if (next == null)
                {
                    errors.Add($"Missing upgrade link from {cursor.Original} toward {target.Original}.");
                    return Array.Empty<MigrationUpgradeScript>();
                }

                if (!visited.Add(next.To))
                {
                    errors.Add($"Upgrade path loops at version {next.To.Original}.");
                    return Array.Empty<MigrationUpgradeScript>();
                }

                required.Add(next.Script);
                cursor = next.To;
            }

            return required;
        }

        private static IReadOnlyList<UpgradeEdge> ParseEdges(
            IReadOnlyList<MigrationUpgradeScript> scripts,
            List<string> errors)
        {
            var edges = new List<UpgradeEdge>();

            foreach (var script in scripts)
            {
                if (script == null)
                {
                    errors.Add("Upgrade script entry is null.");
                    continue;
                }

                var fromText = script.FromVersion;
                var toText = script.ToVersion;

                if (string.IsNullOrWhiteSpace(fromText) || string.IsNullOrWhiteSpace(toText))
                {
                    var match = UpgradeFileNameRegex.Match(script.FileName ?? string.Empty);
                    if (!match.Success)
                    {
                        errors.Add($"Malformed upgrade filename: {script.FileName}");
                        continue;
                    }

                    fromText = match.Groups["from"].Value;
                    toText = match.Groups["to"].Value;
                }

                if (!VersionKey.TryParse(fromText, out var from) || from.IsEmpty)
                {
                    errors.Add($"Malformed from-version for {script.FileName}: {fromText}");
                    continue;
                }

                if (!VersionKey.TryParse(toText, out var to) || to.IsEmpty)
                {
                    errors.Add($"Malformed to-version for {script.FileName}: {toText}");
                    continue;
                }

                if (to.CompareTo(from) <= 0)
                    errors.Add($"Downgrade or no-op upgrade script detected: {script.FileName} ({fromText} -> {toText}).");

                edges.Add(new UpgradeEdge(from, to, script, fromText ?? string.Empty, toText ?? string.Empty));
            }

            return edges;
        }

        private static void DetectDuplicateLinks(IReadOnlyList<UpgradeEdge> edges, List<string> errors)
        {
            foreach (var group in edges.GroupBy(edge => (edge.From, edge.To)))
            {
                if (group.Count() <= 1)
                    continue;

                errors.Add(
                    $"Duplicate upgrade link {group.Key.From.Original} -> {group.Key.To.Original}: " +
                    string.Join(", ", group.Select(edge => edge.Script.FileName)));
            }
        }

        private static void DetectAmbiguousBranches(IReadOnlyList<UpgradeEdge> edges, List<string> errors)
        {
            foreach (var group in edges.GroupBy(edge => edge.From))
            {
                var distinctTargets = group.Select(edge => edge.To).Distinct().ToArray();
                if (distinctTargets.Length <= 1)
                    continue;

                errors.Add(
                    $"Ambiguous upgrade branch from {group.Key.Original}: " +
                    string.Join(", ", group.Select(edge => $"{edge.ToVersion} ({edge.Script.FileName})")));
            }
        }

        private static UpgradeEdge? FindHighestReachable(VersionKey current, IReadOnlyList<UpgradeEdge> edges)
        {
            var byFrom = edges
                .Where(edge => edge.To.CompareTo(edge.From) > 0)
                .GroupBy(edge => edge.From)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var queue = new Queue<VersionKey>();
            var visited = new HashSet<VersionKey> { current };
            UpgradeEdge? highest = null;

            queue.Enqueue(current);

            while (queue.Count > 0)
            {
                var version = queue.Dequeue();
                if (!byFrom.TryGetValue(version, out var nextEdges))
                    continue;

                foreach (var edge in nextEdges)
                {
                    if (!visited.Add(edge.To))
                        continue;

                    if (highest == null || edge.To.CompareTo(highest.To) > 0)
                        highest = edge;

                    queue.Enqueue(edge.To);
                }
            }

            return highest;
        }

        private static MigrationUpgradePlan CreatePlan(
            string currentDbVersion,
            string targetDbVersion,
            IReadOnlyList<MigrationUpgradeScript> requiredScripts,
            IReadOnlyList<string> warnings,
            IReadOnlyList<string> errors,
            bool isNoOp)
        {
            return new MigrationUpgradePlan
            {
                CurrentDbVersion = currentDbVersion ?? string.Empty,
                TargetDbVersion = targetDbVersion ?? string.Empty,
                RequiredScripts = requiredScripts.ToArray(),
                Warnings = warnings.ToArray(),
                Errors = errors.ToArray(),
                IsNoOp = isNoOp
            };
        }

        private sealed record UpgradeEdge(
            VersionKey From,
            VersionKey To,
            MigrationUpgradeScript Script,
            string FromVersion,
            string ToVersion);

        private readonly struct VersionKey : IComparable<VersionKey>, IEquatable<VersionKey>
        {
            private readonly int[] _parts;

            private VersionKey(string original, int[] parts)
            {
                Original = original;
                _parts = parts;
            }

            public string Original { get; }
            public bool IsEmpty => _parts == null || _parts.Length == 0;

            public static bool TryParse(string? value, out VersionKey version)
            {
                version = default;
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                var clean = value.Trim();
                if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    clean = clean[1..];

                var textParts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (textParts.Length == 0)
                    return false;

                var parts = new int[textParts.Length];
                for (var i = 0; i < textParts.Length; i++)
                {
                    if (!int.TryParse(textParts[i], out parts[i]))
                        return false;
                }

                version = new VersionKey(value.Trim(), parts);
                return true;
            }

            public int CompareTo(VersionKey other)
            {
                var length = Math.Max(_parts?.Length ?? 0, other._parts?.Length ?? 0);
                for (var i = 0; i < length; i++)
                {
                    var left = _parts != null && i < _parts.Length ? _parts[i] : 0;
                    var right = other._parts != null && i < other._parts.Length ? other._parts[i] : 0;
                    var comparison = left.CompareTo(right);
                    if (comparison != 0)
                        return comparison;
                }

                return 0;
            }

            public bool Equals(VersionKey other) => CompareTo(other) == 0;

            public override bool Equals(object? obj) => obj is VersionKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                var trimmedLength = _parts?.Length ?? 0;
                while (trimmedLength > 0 && _parts![trimmedLength - 1] == 0)
                    trimmedLength--;

                for (var i = 0; i < trimmedLength; i++)
                    hash.Add(_parts![i]);

                return hash.ToHashCode();
            }
        }
    }
}
