// Utilities/Diagnostics/EarlyLoginFailures.Shim.cs
using System;

namespace Utilities.Diagnostics   // <— match the real namespace of EarlyLoginFailures
{
    public static partial class EarlyLoginFailures
    {
        // ----- Enum overloads that forward to the existing string-based APIs -----

        public static void Record(EarlyFailType category, string message, Exception? ex = null, string? relatedFile = null)
            => Record(category.ToString(), message, ex, relatedFile);

        public static void Record(EarlyFailType category, Exception ex, string? relatedFile = null)
            => Record(category.ToString(), ex.Message, ex, relatedFile);

        public static void Record(EarlyFailType category)
            => Record(category.ToString(), "no message");

        public static void Quarantine(string fullPath, EarlyFailType reason)
            => Quarantine(fullPath, reason.ToString());

        // Some callers may have the params flipped; cover that too:
        public static void Quarantine(EarlyFailType reason, string fullPath)
            => Quarantine(fullPath, reason.ToString());
    }
}
