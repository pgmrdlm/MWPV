using System;
using System.Linq;

namespace MWPV.Services.AppLifecycle
{
    public static class AppStartupDetector
    {
        private const string LegacyMigrationFlagName = "migration_flag";
        private const string UpgradeFlag = "--upgrade";
        private const string MigrationFlag = "--migration";
        private const string MigrationFlagPathPrefix = "--migration-flag=";
        private const string TargetVersionPrefix = "--target-version=";

        public static AppStartupContext Detect(
            string[]? args,
            bool databaseExists,
            bool upgradeFlagFileExists = false,
            string? defaultUpgradeFlagFilePath = null)
        {
            var parsed = ParseArgs(args);
            var isUpgrade = parsed.IsUpgrade || upgradeFlagFileExists;

            if (isUpgrade)
            {
                return new AppStartupContext
                {
                    RunMode = AppRunMode.Upgrade,
                    UpgradeFlagFilePath = parsed.UpgradeFlagFilePath ?? defaultUpgradeFlagFilePath,
                    RequestedTargetVersion = parsed.TargetVersion,
                    LaunchedByInstaller = parsed.IsUpgrade,
                    ShouldExitAfterUpgrade = parsed.IsUpgrade
                };
            }

            return new AppStartupContext
            {
                RunMode = databaseExists ? AppRunMode.Normal : AppRunMode.FreshInstall
            };
        }

        private static ParsedStartupArgs ParseArgs(string[]? args)
        {
            if (args == null || args.Length == 0)
                return ParsedStartupArgs.Empty;

            var parsed = ParsedStartupArgs.Empty;

            foreach (var arg in args.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (string.Equals(arg, UpgradeFlag, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, MigrationFlag, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, LegacyMigrationFlagName, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = parsed with { IsUpgrade = true };
                    continue;
                }

                if (arg.StartsWith(MigrationFlagPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = parsed with
                    {
                        IsUpgrade = true,
                        UpgradeFlagFilePath = arg[MigrationFlagPathPrefix.Length..]
                    };
                    continue;
                }

                var legacyPrefix = LegacyMigrationFlagName + "=";
                if (arg.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = parsed with
                    {
                        IsUpgrade = true,
                        UpgradeFlagFilePath = arg[legacyPrefix.Length..]
                    };
                    continue;
                }

                if (arg.StartsWith(TargetVersionPrefix, StringComparison.OrdinalIgnoreCase))
                    parsed = parsed with { TargetVersion = arg[TargetVersionPrefix.Length..] };
            }

            return parsed;
        }

        private sealed record ParsedStartupArgs(
            bool IsUpgrade,
            string? UpgradeFlagFilePath,
            string? TargetVersion)
        {
            public static ParsedStartupArgs Empty { get; } = new(false, null, null);
        }
    }
}
