using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Security
{
    /// <summary>
    /// Centralized input validation & normalization for names and free text.
    /// - Category/item names: stricter (forbid quotes, angle brackets, pipe, etc.)
    /// - Free text: collapses whitespace, strips control chars, optionally forbids a small set (e.g., '<', '>')
    /// </summary>
    public static class InputGuards
    {
        // collapse any whitespace run to a single space
        private static readonly Regex CollapseSpaces = new(@"\s+", RegexOptions.Compiled);

        // remove ALL control chars except CR/LF/TAB
        private static readonly Regex StripControl = new(@"[\p{C}&&[^\r\n\t]]", RegexOptions.Compiled);

        // base forbidden characters for "names"
        private static readonly char[] ForbiddenBase = new[] { '\'', '\"', ';', '\\', '`', '<', '>', '|' };

        public readonly struct NameCheck
        {
            public bool IsValid { get; }
            public string CleanName { get; }
            public string? Error { get; }

            public NameCheck(bool ok, string name, string? error)
            {
                IsValid = ok;
                CleanName = name;
                Error = error;
            }

            public static NameCheck Ok(string name) => new(true, name, null);
            public static NameCheck Fail(string message) => new(false, string.Empty, message);
        }

        /// <summary>
        /// Validate a category/item name.
        /// </summary>
        public static NameCheck ValidateCategoryName(
            string raw,
            int minLen,
            int maxLen,
            string? extraAllowed = null,                 // chars to *unblock* from ForbiddenBase (e.g., ".," if ever desired)
            IEnumerable<string>? bannedNames = null)     // optional banned-name list
        {
            if (raw is null)
                return NameCheck.Fail("Name is required.");

            string s = raw.Trim();
            s = CollapseSpaces.Replace(s, " ");
            s = StripControl.Replace(s, string.Empty);

            if (s.Length < minLen)
                return NameCheck.Fail($"Category name must be at least {minLen} characters.");
            if (s.Length > maxLen)
                return NameCheck.Fail($"Category name must be {maxLen} characters or fewer.");

            var forbidden = BuildForbidden(extraAllowed);
            if (s.IndexOfAny(forbidden) >= 0)
                return NameCheck.Fail("Category name contains characters that aren’t allowed (e.g., quotes, angle brackets, pipe).");

            if (bannedNames != null && bannedNames.Contains(s, StringComparer.OrdinalIgnoreCase))
                return NameCheck.Fail("That category name isn’t allowed. Please choose a different name.");

            return NameCheck.Ok(s);
        }

        /// <summary>
        /// Normalize free-form text: trim, collapse spaces, strip control chars,
        /// drop a small "forbidden" set (default: &lt; &gt; |), and cap length.
        /// </summary>
        public static string? NormalizeFreeText(string? raw, int maxLen, string? extraAllowed = null)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Trim();
            s = CollapseSpaces.Replace(s, " ");
            s = StripControl.Replace(s, string.Empty);

            // Remove forbidden characters from free-text (default: <, >, |)
            var forbidden = BuildForbiddenForFreeText(extraAllowed); // default forbids '<', '>', '|'
            if (forbidden.Length > 0)
            {
                s = new string(s.Where(ch => Array.IndexOf(forbidden, ch) < 0).ToArray());
            }

            if (maxLen > 0 && s.Length > maxLen)
                s = s.Substring(0, maxLen);

            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        // Build forbidden set for names, optionally "unblocking" characters in extraAllowed
        private static char[] BuildForbidden(string? extraAllowed)
        {
            if (string.IsNullOrEmpty(extraAllowed)) return ForbiddenBase;
            var allow = extraAllowed.ToHashSet();
            return ForbiddenBase.Where(c => !allow.Contains(c)).ToArray();
        }

        // For free text we default to a MUCH smaller forbidden set (just the “dangerous” trio).
        // Pass extraAllowed: ".," etc. if you ever add more to this set in the future.
        private static char[] BuildForbiddenForFreeText(string? extraAllowed)
        {
            var baseSet = new[] { '<', '>', '|' }; // keep it tiny for UX; commas/periods stay allowed by default
            if (string.IsNullOrEmpty(extraAllowed)) return baseSet;

            var allow = extraAllowed.ToHashSet();
            return baseSet.Where(c => !allow.Contains(c)).ToArray();
        }
    }
}
