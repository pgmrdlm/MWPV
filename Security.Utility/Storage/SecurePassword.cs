// File: Security.Utility/Storage/SecurePassword.cs
// FULL REWRITE
//
// Changes:
// - Keep existing Generate() behavior (strong specials set) for backward compatibility.
// - Add a "Compatible" password generator that uses a conservative special-char set
//   (more likely to be accepted by picky sites).
// - Refactor generation into a single internal method that accepts the specials set.

using System;
using System.Linq;
using System.Security.Cryptography;
using Security.Utility.Wiping; // SensitiveDataCleaner lives here (may be used by callers)

namespace Security.Utility.Storage
{
    public static class SecurePassword
    {
        private const int MinimumLength = 8;

        // Existing (strong) specials set (backward compatible with current behavior)
        private const string StrongSpecials = "!@#$%^&*()-_=+[]{}:,.?";

        // Conservative specials set for compatibility with picky sites.
        // (Avoids quotes, slashes, backticks, spaces, braces/brackets, etc.)
        private const string CompatibleSpecials = "!@#$%&*()-_=+.?";

        public static bool IsPasswordValid(string password, string verifyPassword, out string errorMessage)
            => IsPasswordValid(password, verifyPassword, MinimumLength, out errorMessage);

        public static bool IsPasswordValid(string password, string verifyPassword, int minimumLength, out string errorMessage)
        {
            errorMessage = "";
            minimumLength = Math.Max(minimumLength, MinimumLength);

            if (password != verifyPassword)
            {
                errorMessage = "Passwords do not match.";
                return false;
            }

            if (password.Length < minimumLength)
            {
                errorMessage = $"Password must be at least {minimumLength} characters long.";
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

        /// <summary>
        /// Validates password policy and returns only a Security.Utility technical result.
        /// No user-facing message text, password value, exception text, or caller action
        /// is returned.
        /// </summary>
        public static SecurityUtilityResult ValidatePasswordPolicyResult(string password, string verifyPassword)
            => ValidatePasswordPolicyResult(password, verifyPassword, MinimumLength);

        /// <summary>
        /// Validates password policy and returns only a Security.Utility technical result.
        /// No user-facing message text, password value, exception text, or caller action
        /// is returned.
        /// </summary>
        public static SecurityUtilityResult ValidatePasswordPolicyResult(
            string password,
            string verifyPassword,
            int minimumLength)
        {
            if (password is null || verifyPassword is null)
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);

            minimumLength = Math.Max(minimumLength, MinimumLength);

            if (password != verifyPassword)
                return Result(SecurityUtilityReturnCode.PasswordPolicyFailed, SecurityUtilityResultKind.Failure);

            if (password.Length < minimumLength)
                return Result(SecurityUtilityReturnCode.PasswordPolicyFailed, SecurityUtilityResultKind.Failure);

            int conditionsMet = 0;
            if (password.Any(char.IsUpper)) conditionsMet++;
            if (password.Any(char.IsLower)) conditionsMet++;
            if (password.Any(char.IsDigit)) conditionsMet++;
            if (password.Any(ch => !char.IsLetterOrDigit(ch))) conditionsMet++;

            return conditionsMet >= 3
                ? Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success)
                : Result(SecurityUtilityReturnCode.PasswordPolicyFailed, SecurityUtilityResultKind.Failure);
        }

        // Generate a password into target[], meeting >=3/4 categories (strong specials set)
        public static void Generate(ref char[] target, int length)
        {
            GenerateInternal(ref target, length, StrongSpecials);
        }

        // Generate a password into target[], meeting >=3/4 categories (compatible specials set)
        public static void GenerateCompatible(ref char[] target, int length)
        {
            GenerateInternal(ref target, length, CompatibleSpecials);
        }

        // Generate an alphanumeric password into target[], always including lower, upper, and digit.
        public static void GenerateAlphanumeric(ref char[] target, int length)
        {
            if (length < MinimumLength)
                throw new ArgumentException($"Password length must be at least {MinimumLength} characters.", nameof(length));

            const string lowers = "abcdefghijklmnopqrstuvwxyz";
            const string uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "0123456789";

            var L = lowers.ToCharArray();
            var U = uppers.ToCharArray();
            var D = digits.ToCharArray();

            if (target == null || target.Length != length)
                target = new char[length];

            int pos = 0;
            target[pos++] = RandomFrom(L);
            target[pos++] = RandomFrom(U);
            target[pos++] = RandomFrom(D);

            char[] union = L.Concat(U).Concat(D).ToArray();
            while (pos < length)
                target[pos++] = RandomFrom(union);

            Shuffle(target);
        }

        // Optional UI convenience. Avoid if you don’t need it.
        public static string GenerateAsString(int length)
        {
            char[] buf = null!;
            Generate(ref buf, length);
            try { return new string(buf); }
            finally { Array.Clear(buf, 0, buf.Length); }
        }

        // Optional UI convenience: compatible generator
        public static string GenerateCompatibleAsString(int length)
        {
            char[] buf = null!;
            GenerateCompatible(ref buf, length);
            try { return new string(buf); }
            finally { Array.Clear(buf, 0, buf.Length); }
        }

        // Optional UI convenience: alphanumeric generator
        public static string GenerateAlphanumericAsString(int length)
        {
            char[] buf = null!;
            GenerateAlphanumeric(ref buf, length);
            try { return new string(buf); }
            finally { Array.Clear(buf, 0, buf.Length); }
        }

        // --- internal implementation ---

        private static void GenerateInternal(ref char[] target, int length, ReadOnlySpan<char> specials)
        {
            if (length < MinimumLength)
                throw new ArgumentException($"Password length must be at least {MinimumLength} characters.", nameof(length));

            // Char sets
            const string lowers = "abcdefghijklmnopqrstuvwxyz";
            const string uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "0123456789";

            // Convert to arrays once
            var L = lowers.ToCharArray();
            var U = uppers.ToCharArray();
            var D = digits.ToCharArray();
            var S = specials.ToArray();

            // We'll always include at least three categories; pick which three randomly
            // Categories index map: 0=L,1=U,2=D,3=S
            int[] cats = { 0, 1, 2, 3 };
            Shuffle(cats); // RNG shuffle
            var chosen = cats[..3]; // first three categories after shuffle

            // Prepare result buffer
            if (target == null || target.Length != length)
                target = new char[length];

            int pos = 0;

            // Ensure at least one from each chosen category
            foreach (int c in chosen)
                target[pos++] = RandomFrom(c switch { 0 => L, 1 => U, 2 => D, _ => S });

            // Fill the rest from the union of all four sets
            char[] union = L.Concat(U).Concat(D).Concat(S).ToArray();
            while (pos < length)
                target[pos++] = RandomFrom(union);

            // Final shuffle so required chars are not predictable positions
            Shuffle(target);
        }

        // --- helpers ---

        private static char RandomFrom(ReadOnlySpan<char> set)
        {
            int i = RandomNumberGenerator.GetInt32(set.Length);
            return set[i];
        }

        private static void Shuffle<T>(T[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        private static SecurityUtilityResult Result(
            SecurityUtilityReturnCode code,
            SecurityUtilityResultKind kind)
            => new()
            {
                Code = code,
                Kind = kind
            };
    }
}
