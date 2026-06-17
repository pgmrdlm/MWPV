namespace MWPV.Services.AppLifecycle
{
    public sealed record AppStartupContext
    {
        public AppRunMode RunMode { get; init; } = AppRunMode.Normal;
        public string? UpgradeFlagFilePath { get; init; }
        public string? RequestedTargetVersion { get; init; }
        public bool LaunchedByInstaller { get; init; }
        public bool ShouldExitAfterUpgrade { get; init; }

        public static AppStartupContext Normal { get; } = new();
    }
}
