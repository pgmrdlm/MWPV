using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Utilities.Diagnostics
{
    public static partial class EarlyLoginFailures
    {
        // allow 1+ digits for each numeric; accept optional BOM / whitespace
        public const string ElogHeaderPattern =
            @"^\s*\uFEFF?ELOG(\d+)JSON\|v(\d+)\|n(\d+)\|r([A-Fa-f0-9]{32})\s*$";

        public static readonly Regex ElogHeaderRegex =
            new Regex(ElogHeaderPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

#if DEBUG
        [Conditional("DEBUG")]
        public static void Dbg(string msg, [CallerMemberName] string? member = null)
            => Debug.WriteLine($"[EARLY][{member}] {msg}");
#else
        [Conditional("DEBUG")]
        public static void Dbg(string msg, [CallerMemberName] string? member = null) { }
#endif
    }
}
