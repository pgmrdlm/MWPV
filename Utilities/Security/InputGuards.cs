// Utilities/Security/InputGuards.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Security
{
    /// <summary>
    /// Centralized, side-effect-free input validation/sanitization.
    /// Two overloads of Validate():
    ///   • Strict (names/passwords)  -> Validate(string?, int minLen, int maxLen, …)
    ///   • Freeform (descriptions)   -> Validate(string?, int maxLen, bool allowLineBreaks, …)
    ///
    /// Also char[]/ReadOnlySpan<char> overload for secrets (no string concat).
    /// Returns a ValidationResult with IsValid, Clean, and Error.
    /// </summary>
    public static class InputGuards
    {
        // Collapses any run of whitespace to a single space
        private static readonly Regex CollapseSpaces = new(@"\s+", RegexOptions.Compiled);

        // Strip all control chars except CR/LF/TAB
        private static readonly Regex StripControl = new(@"[\p{C}&&[^\r\n\t]]", RegexOptions.Compiled);

        // Default forbidden set for strict inputs (names/passwords)
        private static readonly char[] DefaultForbiddenStrict =
            new[] { '\'', '\"', ';', '\\', '`', '<', '>', '|' };

        // Default forbidden set for freeform inputs (keep angle brackets blocked)
        private static readonly char[] DefaultForbiddenFreeform =
            new[] { '<', '>', '`', '|' };

        /* ---------------- Result type ---------------- */

        public readonly struct ValidationResult
        {
            public bool IsValid { get; }
            public string? Clean { get; }
            public string? Error { get; }

            public ValidationResult(bool ok, string? clean, string? error)
            {
                IsValid = ok; Clean = clean; Error = error;
            }
        }

        /* ---------------- Strict: names / passwords ---------------- */

        public static ValidationResult Validate(
            string? text,
            int minLen,
            int maxLen,
            ReadOnlySpan<char> extraForbidden = default,
            IEnumerable<string>? bannedTerms = null)
        {
            // Normalize (do NOT mutate caller's text)
            string s = NormalizeForSingleLine(text);

            // Length checks first (friendly messages)
            if (s.Length < minLen)
                return Fail($"Must be at least {minLen} characters long.");
            if (s.Length > maxLen)
                return Fail($"Must be {maxLen} characters or fewer.");

            // Forbidden characters
            if (ContainsAny(s, DefaultForbiddenStrict) || ContainsAny(s, extraForbidden))
                return Fail("Contains characters that aren’t allowed (e.g., quotes, angle brackets, pipe).");

            // Banned terms (case-insensitive)
            if (bannedTerms != null && bannedTerms.Any(bt => bt != null && s.Equals(bt, StringComparison.OrdinalIgnoreCase)))
                return Fail("That value isn’t allowed. Please choose a different one.");

            return Ok(s);
        }

        // Secret/Password overload that avoids string allocation of the original input
        public static ValidationResult Validate(
            ReadOnlySpan<char> secret,
            int minLen,
            int maxLen,
            ReadOnlySpan<char> extraForbidden = default,
            IEnumerable<string>? bannedTerms = null)
        {
            // Copy into a temp normalized string (we must for regex/length rules)
            string s = NormalizeForSingleLine(new string(secret));

            if (s.Length < minLen)
                return Fail($"Must be at least {minLen} characters long.");
            if (s.Length > maxLen)
                return Fail($"Must be {maxLen} characters or fewer.");

            if (ContainsAny(s, DefaultForbiddenStrict) || ContainsAny(s, extraForbidden))
                return Fail("Contains characters that aren’t allowed (e.g., quotes, angle brackets, pipe).");

            if (bannedTerms != null && bannedTerms.Any(bt => bt != null && s.Equals(bt, StringComparison.OrdinalIgnoreCase)))
                return Fail("That value isn’t allowed. Please choose a different one.");

            return Ok(s);
        }

        /* ---------------- Freeform: descriptions / notes ---------------- */

        public static ValidationResult Validate(
            string? text,
            int maxLen,
            bool allowLineBreaks,
            ReadOnlySpan<char> extraForbidden = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Ok(null); // treat blank as null

            string s = text.Trim();
            s = CollapseSpaces.Replace(s, " ");
            s = StripControl.Replace(s, string.Empty);

            // Optionally remove CR/LF if not allowed
            if (!allowLineBreaks)
                s = s.Replace("\r", "").Replace("\n", "");

            if (s.Length > maxLen)
                s = s.Substring(0, maxLen);

            if (ContainsAny(s, DefaultForbiddenFreeform) || ContainsAny(s, extraForbidden))
                return Fail("Contains characters that aren’t allowed in description (e.g., angle brackets, pipe).");

            return Ok(string.IsNullOrWhiteSpace(s) ? null : s);
        }

        /* ---------------- Helpers ---------------- */

        private static string NormalizeForSingleLine(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string s = raw.Trim();
            s = CollapseSpaces.Replace(s, " ");
            s = StripControl.Replace(s, string.Empty);
            s = s.Replace("\r", " ").Replace("\n", " "); // force single line
            return s;
        }

        private static bool ContainsAny(string s, ReadOnlySpan<char> chars)
        {
            for (int i = 0; i < chars.Length; i++)
            {
                if (s.IndexOf(chars[i]) >= 0) return true;
            }
            return false;
        }

        private static ValidationResult Ok(string? clean) => new(true, clean, null);
        private static ValidationResult Fail(string msg) => new(false, null, msg);
    }
}