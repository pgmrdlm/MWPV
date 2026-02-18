// File: MWPV/Session/AppStatus.cs
using System;

namespace MWPV.Session
{
    /// <summary>
    /// App-wide, in-memory session status for MWPV.
    ///
    /// Rules:
    /// - NOT encrypted (not sensitive by itself).
    /// - Single source of truth: set CurrentTab in exactly one place (the tab traffic-cop).
    /// - Everyone else only reads it.
    /// </summary>
    public static class AppStatus
    {
        // Minimal tab identity enum. Add values as tabs become relevant.
        public enum TabId
        {
            Unknown = 0,
            Basic = 1,
            Accounts = 2,
            BankCards = 3,
            Logs = 4
        }

        /// <summary>
        /// The currently active main tab.
        /// Set this ONLY from the tab selection-changed handler.
        /// </summary>
        public static TabId CurrentTab { get; set; } = TabId.Unknown;

        /// <summary>
        /// Convenience helper for your inactivity logic.
        /// </summary>
        // AppStatus.cs
        public static bool IsBasicOpen { get; internal set; }


        /// <summary>
        /// Optional helper to reset on logout/exit if you want.
        /// </summary>
        public static void Reset()
        {
            CurrentTab = TabId.Unknown;
        }
    }
}
