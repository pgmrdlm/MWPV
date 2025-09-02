using System;
using System.Collections.Generic;
using System.Linq;

namespace MWPV.Utilities.Security
{
    public enum PasswordStrength
    {
        VeryWeak = 0,
        Weak = 1,
        Fair = 2,
        Strong = 3,
        VeryStrong = 4
    }

    public sealed class PasswordStrengthResult
    {
        public PasswordStrength Strength { get; init; }
        public double Score01 { get; init; } // 0.0–1.0 for meter binding
        public string[] Suggestions { get; init; } = Array.Empty<string>();
        public int Length { get; init; }
    }

    public static class PasswordStrengthEvaluator
    {
        private static readonly string[] CommonBad =
        {
            "password","123456","qwerty","letmein",
            "admin","welcome","iloveyou","monkey"
        };

        public static PasswordStrengthResult Evaluate(ReadOnlySpan<char> pw)
        {
            int len = pw.Length;
            bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false, hasWhitespace = false;
            bool repeatedRuns = false;
            int runs = 1;

            for (int i = 0; i < pw.Length; i++)
            {
                char c = pw[i];
                if (char.IsLower(c)) hasLower = true;
                else if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (char.IsWhiteSpace(c)) hasWhitespace = true;
                else hasSymbol = true;

                if (i > 0)
                {
                    if (pw[i] == pw[i - 1]) runs++;
                    else runs = 1;
                    if (runs >= 3) repeatedRuns = true;
                }
            }

            // quick common check (case-insensitive)
            var lower = ToLowerFast(pw);
            bool isCommon = CommonBad.Any(b =>
                new string(lower).Contains(b, StringComparison.Ordinal));

            int classes = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
            double raw = 0;

            // contributions
            raw += Math.Min(0.5, len / 20.0);        // up to 0.5 for length
            raw += Math.Min(0.35, classes * 0.09);   // up to ~0.36 for char variety

            // penalties
            if (isCommon) raw -= 0.35;
            if (repeatedRuns) raw -= 0.1;
            if (hasWhitespace) raw -= 0.08;
            if (len < 8) raw -= 0.15;

            double score = Math.Clamp(raw, 0.0, 1.0);

            PasswordStrength band =
                score < 0.20 ? PasswordStrength.VeryWeak :
                score < 0.40 ? PasswordStrength.Weak :
                score < 0.65 ? PasswordStrength.Fair :
                score < 0.85 ? PasswordStrength.Strong :
                               PasswordStrength.VeryStrong;

            var tips = new List<string>();
            if (len < 14) tips.Add("Use 14+ characters.");
            if (classes < 3) tips.Add("Mix upper/lower, digits, and symbols.");
            if (isCommon) tips.Add("Avoid common phrases or patterns.");
            if (repeatedRuns) tips.Add("Avoid repeating the same character.");
            if (hasWhitespace) tips.Add("Avoid spaces or tabs.");
            if (tips.Count == 0) tips.Add("Nice! This looks strong.");

            Array.Clear(lower, 0, lower.Length); // wipe temp buffer

            return new PasswordStrengthResult
            {
                Strength = band,
                Score01 = score,
                Suggestions = tips.ToArray(),
                Length = len
            };
        }

        private static char[] ToLowerFast(ReadOnlySpan<char> pw)
        {
            var buf = new char[pw.Length];
            for (int i = 0; i < pw.Length; i++) buf[i] = char.ToLowerInvariant(pw[i]);
            return buf;
        }
    }
}
