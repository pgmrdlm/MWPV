// Security.Utility/Validation/EmailValidator.cs
using System;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Security.Utility.Validation
{
    public enum EmailCheck { Ok, Empty, BadFormat, BadLength }

    public static class EmailValidator
    {
        private static readonly Regex Basic =
            new(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,63}$", RegexOptions.Compiled);

        public static EmailCheck IsLikelyEmail(string? input, out string message, bool allowIdn = true)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) { message = "Email is required."; return EmailCheck.Empty; }

            var s = input.Trim();
            if (!Basic.IsMatch(s)) { message = "Please enter a valid email address."; return EmailCheck.BadFormat; }

            var at = s.LastIndexOf('@');
            var local = s[..at];
            var domain = s[(at + 1)..];

            if (s.Length > 254 || local.Length > 64) { message = "Email is too long."; return EmailCheck.BadLength; }
            if (local.StartsWith(".") || local.EndsWith(".") || domain.StartsWith(".") || domain.EndsWith(".")
                || local.Contains("..") || domain.Contains(".."))
            { message = "Dots in invalid positions."; return EmailCheck.BadFormat; }

            if (allowIdn)
            {
                try
                {
                    domain = new IdnMapping().GetAscii(domain);
                    s = $"{local}@{domain}";
                }
                catch { message = "Domain has invalid characters."; return EmailCheck.BadFormat; }
            }

            try { _ = new MailAddress(s); return EmailCheck.Ok; }
            catch { message = "Please enter a valid email address."; return EmailCheck.BadFormat; }
        }
    }
}
