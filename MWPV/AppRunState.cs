namespace MWPV
{
    internal static class AppRunState
    {
        internal static bool DbOpenedThisRun;   // set to true after valid login
        internal static bool EndLogged;         // prevents duplicate SESSION_END
    }
}
