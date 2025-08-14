// Utilities/Helpers/KeyArchiveValidator.cs
using System;
using System.IO;

namespace Utilities.Helpers
{
    /// <summary>
    /// Validates that a path points to a plausible 7-Zip key archive.
    /// - Pure helper: no UI, no app-logic, no logging side effects.
    /// - Callers can translate ResultCode to user messages and (optionally) log.
    ///
    /// Example:
    ///   var rc = KeyArchiveValidator.Validate(path, out var message);
    ///   if (rc != KeyArchiveValidator.ResultCode.Ok) {
    ///       // show message via ErrorHandler.Info(...), and/or EarlyLoginFailures.Record(KeyArchiveValidator.ToEventCode(rc), ...);
    ///       return;
    ///   }
    /// </summary>
    public static class KeyArchiveValidator
    {
        // 7z magic signature (first 6 bytes): 37 7A BC AF 27 1C
        private static readonly byte[] Sig7z = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

        public enum ResultCode
        {
            Ok = 0,
            MissingPath,
            NotFile,
            Not7zExtension,
            TooSmall,
            BadSignature,
            ReadError,
            UnknownError
        }

        /// <summary>
        /// Quick validation pipeline: existence → extension → size → signature.
        /// Returns a ResultCode and a friendly user-facing message when not Ok.
        /// </summary>
        public static ResultCode Validate(string? path, out string? userMessage, bool require7zExtension = true)
        {
            userMessage = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                userMessage = "Please select Key File Location";
                return ResultCode.MissingPath;
            }

            if (!File.Exists(path))
            {
                userMessage = "We couldn't find that file. Pick the .7z key file you created earlier.";
                return ResultCode.NotFile;
            }

            if (require7zExtension && !".7z".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                userMessage = "That file isn’t a .7z archive. Please select your MWPV key file (ends with .7z).";
                return ResultCode.Not7zExtension;
            }

            try
            {
                var fi = new FileInfo(path);
                if (fi.Length < 32)
                {
                    userMessage = "The selected file looks empty or invalid.";
                    return ResultCode.TooSmall;
                }

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> head = stackalloc byte[Sig7z.Length];
                int read = fs.Read(head);
                if (read < Sig7z.Length || !Matches(head, Sig7z))
                {
                    userMessage = "That doesn’t look like a valid 7-Zip file. Pick the .7z key file you created earlier.";
                    return ResultCode.BadSignature;
                }

                return ResultCode.Ok;
            }
            catch (IOException)
            {
                userMessage = "We couldn’t read that file (it may be in use or you lack permission). Close other apps and try again.";
                return ResultCode.ReadError;
            }
            catch
            {
                userMessage = "We couldn’t validate that file.";
                return ResultCode.UnknownError;
            }
        }

        /// <summary>Maps a ResultCode to a short event code suitable for logging.</summary>
        public static string ToEventCode(ResultCode rc) => rc switch
        {
            ResultCode.Ok => "KEYFILE_OK",
            ResultCode.MissingPath => "KeyFileMissingPath",
            ResultCode.NotFile => "KeyFileNotFound",
            ResultCode.Not7zExtension => "KeyFileNot7zExtension",
            ResultCode.TooSmall => "KeyFileTooSmall",
            ResultCode.BadSignature => "KeyFileBadSignature",
            ResultCode.ReadError => "KeyFileReadError",
            _ => "KeyFileUnknownError"
        };

        private static bool Matches(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
