using System;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

public enum EmailCheck
{
    Ok,
    Empty,
    BadFormat,
    BadLength,
}

public static class EmailValidator
{
    // local@domain.tld  — TLD letters only (2..63)
    static readonly Regex Basic =
        new(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,63}$", RegexOptions.Compiled);

    public static EmailCheck IsLikelyEmail(string? input, out string message, bool allowIdn = true)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) { message = "Email is required."; return EmailCheck.Empty; }

        var s = input.Trim();

        // Cheap screen
        if (!Basic.IsMatch(s)) { message = "Please enter a valid email address."; return EmailCheck.BadFormat; }

        // Split parts
        var at = s.LastIndexOf('@');
        var local = s[..at];
        var domain = s[(at + 1)..];

        // Length limits
        if (s.Length > 254 || local.Length > 64) { message = "Email is too long."; return EmailCheck.BadLength; }

        // Dots/edges
        if (local.StartsWith(".") || local.EndsWith(".") || domain.StartsWith(".") || domain.EndsWith(".")
            || local.Contains("..") || domain.Contains(".."))
        { message = "Dots in invalid positions."; return EmailCheck.BadFormat; }

        // IDN → ASCII for deeper parsing
        if (allowIdn)
        {
            try
            {
                var idn = new IdnMapping();
                domain = idn.GetAscii(domain); // throws if illegal
                s = $"{local}@{domain}";
            }
            catch
            {
                message = "Domain has invalid characters.";
                return EmailCheck.BadFormat;
            }
        }

        // Final parse with System.Net.Mail
        try
        {
            _ = new MailAddress(s);
            return EmailCheck.Ok;
        }
        catch
        {
            message = "Please enter a valid email address.";
            return EmailCheck.BadFormat;
        }
    }
}
