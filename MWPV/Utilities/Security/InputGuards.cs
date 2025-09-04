using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Security.Utility
{
    /// <summary>
    /// Centralized input validation & normalization for names and free text.
    /// - Names: strict (length, allowed char set), single-line.
    /// - Descriptions: human-friendly; allow punctuation, strip control chars, cap length.
    /// </summary>
    public static class InputGuards
    {
        // Collapse runs of spaces/tabs to a single space, but PRESERVE newlines.
        // [^\S\n] matches whitespace that is NOT a newline.
        private static readonly Regex CollapseSpacesButKeepNewlines =
            new(@"[^\S\n]+", RegexOptions.Compiled);

        // Remove ALL control chars EXCEPT CR/LF/TAB (we normalize CR to LF later).
        private static readonly Regex StripControlExceptNewlineTab =
            new(@"[\p{C}&&[^\r\n\t]]", RegexOptions.Compiled);

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
        /// - Trims, strips control chars, collapses whitespace, forces single-line.
        /// - Enforces min/max length.
        /// - Allows letters/digits/space/'-','_','&' (+ any in extraAllowed).
        /// - Optional banned-name list (case-insensitive).
        /// </summary>
        public static NameCheck ValidateCategoryName(
            string? raw,
            int minLen,
            int maxLen,
            string? extraAllowed = null,
            IEnumerable<string>? bannedNames = null)
        {
            if (raw is null)
                return NameCheck.Fail("Category name is required.");

            // Normalize → single line, no control chars, compact spacing.
            string s = raw.Trim();
            s = StripControlExceptNewlineTab.Replace(s, string.Empty);
            s = s.Replace("\r\n", "\n").Replace('\r', '\n'); // normalize line endings
            s = s.Replace('\n', ' ');                        // names must be single-line
            s = CollapseSpacesButKeepNewlines.Replace(s, " ").Trim();

            if (s.Length < minLen)
                return NameCheck.Fail($"Category name must be at least {minLen} characters.");
            if (s.Length > maxLen)
                return NameCheck.Fail($"Category name must be {maxLen} characters or fewer.");

            // Allowed set: letters, digits, space, -, _, &, plus extras
            var allowExtra = (extraAllowed ?? string.Empty).ToHashSet();
            bool Allowed(char c) =>
                char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '&' || allowExtra.Contains(c);

            if (s.Any(ch => !Allowed(ch)))
                return NameCheck.Fail("Name can only contain letters, numbers, spaces, -, _, &.");

            if (bannedNames != null && bannedNames.Contains(s, StringComparer.OrdinalIgnoreCase))
                return NameCheck.Fail("That category name isn’t allowed. Please choose a different name.");

            return NameCheck.Ok(s);
        }

        /// <summary>
        /// Normalize free-form text:
        /// - Trim edges; normalize CR/LF to LF
        /// - Strip control chars (except LF/TAB)
        /// - Collapse runs of spaces/tabs (preserve newlines)
        /// - Remove trailing spaces per line
        /// - Cap to maxLen
        /// Returns null if empty after normalization.
        /// </summary>
        public static string? NormalizeFreeText(string? raw, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Trim();

            // Normalize line endings to \n and strip control chars except \n/\t
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = StripControlExceptNewlineTab.Replace(s, string.Empty);

            // Collapse spaces/tabs, keep newlines
            s = CollapseSpacesButKeepNewlines.Replace(s, " ");

            // Trim trailing spaces per line and overall
            var lines = s.Split('\n').Select(l => l.TrimEnd());
            s = string.Join("\n", lines).Trim();

            if (maxLen > 0 && s.Length > maxLen)
                s = s.Substring(0, maxLen);

            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        /// <summary>
        /// Validates free-text for descriptions/tooltips:
        /// - First normalizes via NormalizeFreeText (preserves newlines)
        /// - Allows normal punctuation (apostrophes, quotes, semicolons, etc.)
        /// - Rejects only if empty (optional) or over maxLen (handled in normalization)
        /// </summary>
        public static (bool IsValid, string? CleanText, string? Error) ValidateDescription(
            string? text,
            int maxLen = 512,
            string? extraAllowed = null) // kept for signature compatibility; not used
        {
            string normalized = NormalizeFreeText(text, maxLen) ?? string.Empty;

            // If you want to allow empty descriptions, treat empty as valid:
            // return (true, normalized, null);
            // Current policy: empty is allowed; caller may replace with the name.
            return (true, normalized, null);
        }
    }
}
