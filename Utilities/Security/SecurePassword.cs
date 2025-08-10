using PasswordGenerator;
using System;

namespace Utilities.Security
{
    public static class SecurePassword
    {
        private const int MinimumLength = 8;
        public static bool IsPasswordValid
            (string password, 
            string verifyPassword, 
            out string errorMessage
            )
        {
            errorMessage = "";

            if (password != verifyPassword)
            {
                errorMessage = "Passwords do not match.";
                return false;
            }

            if (password.Length < MinimumLength)
            {
                errorMessage = "Password must be at least 8 characters long.";
                return false;
            }

            int conditionsMet = 0;
            if (password.Any(char.IsUpper)) conditionsMet++;
            if (password.Any(char.IsLower)) conditionsMet++;
            if (password.Any(char.IsDigit)) conditionsMet++;
            if (password.Any(ch => !char.IsLetterOrDigit(ch))) conditionsMet++;

            if (conditionsMet < 3)
            {
                errorMessage =
                    "Password must meet at least three of the following conditions:\n" +
                    "- At least one uppercase letter\n" +
                    "- At least one lowercase letter\n" +
                    "- At least one digit\n" +
                    "- At least one special character";
                return false;
            }

            return true;
        }

        // 📦 Generate a secure password into a char[] (caller can wipe it)
        public static void Generate(ref char[] target, int length)
        {
            if (length <= 0)
                throw new ArgumentException($"Password length must be at least {MinimumLength} characters.");

            // Initialize Password generator with all character sets
            var generator = new Password(includeLowercase: true, 
                includeUppercase: true,
                includeNumeric: true, 
                includeSpecial: true,
                passwordLength: length);

            string password = generator.Next();

            // Resize or create target buffer
            if (target == null || target.Length != length)
                target = new char[length];

            // Copy to char[] for secure handling
            password.CopyTo(0, target, 0, length);

            // Overwrite the temporary string copy — not perfect, but prevents multiple references
            password = null;
        }

        // Optional: generate and return a string version (for UI convenience ONLY — not secure!)
        public static string GenerateAsString(int length)
        {
            if (length <= 0)
                throw new ArgumentException($"Password length must be at least {MinimumLength} characters.");

            var generator = new Password(includeLowercase: true, 
                includeUppercase: true,
                includeNumeric: true, 
                includeSpecial: true,
                passwordLength: length);

            return generator.Next(); // ❗ You CANNOT wipe this string
        }
    }
}
