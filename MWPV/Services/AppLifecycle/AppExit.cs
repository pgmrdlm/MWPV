namespace MWPV.Services.AppLifecycle
{
    public static class AppExit
    {
        public static AppExitCode FinalCode { get; private set; } = AppExitCode.Success;
        public static string? FinalReason { get; private set; }

        public static void Set(AppExitCode code, string? reason = null)
        {
            FinalCode = code;
            FinalReason = reason;
        }

        public static int ToProcessExitCode(AppExitCode code) => (int)code;

        public static int CurrentProcessExitCode => ToProcessExitCode(FinalCode);

        public static void Shutdown(System.Windows.Application? application, AppExitCode code, string? reason = null)
        {
            Set(code, reason);
            application?.Shutdown(ToProcessExitCode(code));
        }
    }
}
