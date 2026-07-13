using System.Threading;

namespace MWPV.Services
{
    public static class VaultSessionStateService
    {
        private static int _vaultDataChanged;

        public static bool VaultDataChangedThisSession =>
            Volatile.Read(ref _vaultDataChanged) != 0;

        public static void MarkChanged() =>
            Interlocked.Exchange(ref _vaultDataChanged, 1);

        public static void Reset() =>
            Interlocked.Exchange(ref _vaultDataChanged, 0);
    }
}
