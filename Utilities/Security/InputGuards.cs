using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Security.Utility
{
    /// <summary>
    /// Centralized input validation & normalization for names and free text.
    ///
    /// Policy:
    /// - We do NOT silently “fix” dangerous symbols. We normalize whitespace and control chars,
    ///   then VALIDATE & REJECT with a clear error if input still violates the rules.
    ///
    /// Names (categories/items):
    ///   - Strict: min/max length, no quotes/angle-brackets/pipe/backslash/backtick/semicolon, etc.
    ///
    /// Descriptions (tooltips / multi-line text):
    ///   - Normalize (trim, keep newlines, strip control chars except \n/\t).
    ///   - Forbid a small explicit set of risky symbols: <, >, |, `, \, ', ", ;
    ///   - Return (IsValid, CleanText, Error) so UIs can show the message and avoid silent stripping.
    /// </summary>
    public static class InputGuards
    {
        // Collapse runs of spaces/tabs to a single space, but PRESERVE newlines.
        // [^\S\n] matches whitespace that is NOT a newline.
        private static readonly Regex CollapseSpacesButKeepNewlines =
            new(@"[^\S\n]+", RegexOptions.Compiled);

        // Remove ALL control chars EXCEPT CR/LF/TAB (we’ll normalize CR to LF later).
        private static readonly Regex StripControlExceptNewlineTab =
            new(@"[\p{C}&&[^\r\n\t]]", RegexOptions.Compiled);

        // Base forbidden characters for "names"
        private static readonly char[] ForbiddenNameChars =
            { '\'', '\"', ';', '\\', '`', '<', '>', '|' };

        // Forbidden characters for descriptions (explicit, narrow set)
        private static readonly char[] ForbiddenDescriptionChars =
            { '<', '>', '|', '`', '\\', '\'', '\"', ';' };

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
        /// - Trims, collapses internal spaces, strips control chars (except \n/\t which are then removed by collapse).
        /// - Enforces min/max.
        /// - Forbids explicit dangerous symbols.
        /// - Optional banned-name list (case-insensitive).
        /// </summary>
        public static NameCheck ValidateCategoryName(
            string? raw,
            int minLen,
            int maxLen,
            string? extraAllowed = null,                 // chars to *unblock* from the forbidden set
            IEnumerable<string>? bannedNames = null)     // optional disallow list
        {
            if (raw is null)
                return NameCheck.Fail("Name is required.");

            // Normalize: trim, collapse spaces (keep newlines out of names by design)
            string s = raw.Trim();
            s = StripControlExceptNewlineTab.Replace(s, string.Empty);
            s = s.Replace("\r\n", "\n").Replace('\r', '\n'); // normalize line endings
            s = s.Replace('\n', ' ');                        // names should be single-line
            s = CollapseSpacesButKeepNewlines.Replace(s, " ").Trim();

            if (s.Length < minLen)
                return NameCheck.Fail($"Category name must be at least {minLen} characters.");
            if (s.Length > maxLen)
                return NameCheck.Fail($"Category name must be {maxLen} characters or fewer.");

            var forbidden = BuildForbidden(ForbiddenNameChars, extraAllowed);
            if (s.IndexOfAny(forbidden) >= 0)
                return NameCheck.Fail("Category name contains characters that aren’t allowed (e.g., quotes, angle brackets, pipe).");

            if (bannedNames != null && bannedNames.Contains(s, StringComparer.OrdinalIgnoreCase))
                return NameCheck.Fail("That category name isn’t allowed. Please choose a different name.");

            return NameCheck.Ok(s);
        }

        /// <summary>
        /// Normalize free-form text:
        /// - Trim leading/trailing whitespace
        /// - Normalize CR/LF to LF
        /// - Strip control chars (except LF/TAB)
        /// - Collapse runs of spaces/tabs (but PRESERVE newlines)
        /// - Cap to maxLen
        ///
        /// NOTE: This does NOT remove “forbidden” symbols. Use ValidateDescription(...) to enforce policy.
        /// Returns null if the resulting text is empty.
        /// </summary>
        public static string? NormalizeFreeText(string? raw, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Trim();

            // Normalize line endings to \n and strip control chars except \n/\t
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = StripControlExceptNewlineTab.Replace(s, string.Empty);

            // Collapse spaces/tabs but KEEP \n intact
            // (This preserves user paragraphing while preventing weird spacing.)
            s = CollapseSpacesButKeepNewlines.Replace(s, " ");

            // Remove trailing spaces on each line, and trim overall
            var lines = s.Split('\n').Select(l => l.TrimEnd());
            s = string.Join("\n", lines).Trim();

            if (maxLen > 0 && s.Length > maxLen)
                s = s.Substring(0, maxLen);

            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        /// <summary>
        /// Validates free-text for descriptions/tooltips:
        /// - First normalizes via NormalizeFreeText (preserves newlines)
        /// - Then REJECTS if any forbidden symbol is present
        /// - Returns (IsValid, CleanText, Error)
        /// </summary>
        public static (bool IsValid, string? CleanText, string? Error) ValidateDescription(
            string? text,
            int maxLen = 512,
            string? extraAllowed = null) // allow callers to relax a specific char if needed
        {
            string normalized = NormalizeFreeText(text, maxLen) ?? string.Empty;

            // Build forbidden set (optionally unblocking one or more chars)
            var forbidden = BuildForbidden(ForbiddenDescriptionChars, extraAllowed);

            if (normalized.IndexOfAny(forbidden) >= 0)
                return (false, null, "Description contains characters that aren’t allowed (<, >, |, `, \\, ', \", ;).");

            return (true, normalized, null);
        }

        // --- helpers ---

        private static char[] BuildForbidden(char[] baseSet, string? extraAllowed)
        {
            if (string.IsNullOrEmpty(extraAllowed)) return baseSet;
            var allow = extraAllowed.ToHashSet();
            return baseSet.Where(c => !allow.Contains(c)).ToArray();
        }
    }
}
