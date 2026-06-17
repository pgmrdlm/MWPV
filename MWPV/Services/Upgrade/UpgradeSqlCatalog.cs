using System;
using System.Collections.Generic;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeSqlScript
    {
        public string FileName { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string SqlText { get; init; } = string.Empty;
    }

    public sealed class UpgradeSqlCatalog
    {
        public IReadOnlyList<UpgradeSqlScript> UpgradeScripts { get; init; } = Array.Empty<UpgradeSqlScript>();

        public static UpgradeSqlCatalog Empty { get; } = new();
    }
}
