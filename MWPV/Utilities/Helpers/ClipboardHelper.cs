using System;
using MWPV.Services.Security;

namespace MWPV.Utilities.Helpers
{
    /// <summary>
    /// Temporary compatibility wrapper. New sensitive clipboard behavior lives in
    /// SensitiveClipboardService so copy buttons share one ownership record and timer.
    /// </summary>
    public static class ClipboardHelper
    {
        public static TimeSpan DefaultTtl
        {
            get => TimeSpan.FromSeconds(MWPV.Services.AppSettingsService.GetSensitiveClipboardClearSeconds());
            set { }
        }

        public static bool TryCopySensitiveText(string? text, out string reason, TimeSpan? ttlOverride = null, string? tag = null)
        {
            _ = ttlOverride;
            reason = string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                reason = "Empty";
                return false;
            }

            var ok = SensitiveClipboardService.Shared.CopySensitiveText(text, NormalizeReason(tag));
            if (!ok)
                reason = "ClipboardCopyFailed";

            return ok;
        }

        public static void StopTimer()
        {
            SensitiveClipboardService.Shared.ClearIfOwned();
        }

        public static void ClearIfStillMatchesLast(string? tag = null)
        {
            _ = tag;
            SensitiveClipboardService.Shared.ClearIfOwned();
        }

        private static string NormalizeReason(string? tag)
        {
            return tag switch
            {
                "BASIC.PW" => "PasswordCopied",
                "BASIC.USER" => "UsernameCopied",
                "BASIC.URL" => "UrlCopied",
                "BASIC.PIN" => "PinCopied",
                "BASIC.EMAIL" => "EmailCopied",
                "BASIC.PHONE" => "PhoneCopied",
                "BASIC.PRIMARY_ACCOUNT" => "PrimaryAccountNumberCopied",
                "SecurityQuestionAnswer" => "SecurityQuestionAnswerCopied",
                "BANKCARD.NUMBER" => "BankCardNumberCopied",
                "BANKCARD.CVV" => "BankCardCvvCopied",
                "BANKCARD.PIN" => "BankCardPinCopied",
                "ACCOUNT.NUMBER" => "AccountNumberCopied",
                null or "" => "SensitiveClipboardCopied",
                _ => tag
            };
        }
    }
}
