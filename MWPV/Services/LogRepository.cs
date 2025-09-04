// Services/LogRepository.Logging.cs  — add this file to your project
using System;
using System.Threading.Tasks;
using Security.Utility.Logging; // LogSeverity

namespace MWPV.Services
{
    // Ensure your primary LogRepository is declared: public partial class LogRepository
    public partial class LogRepository
    {
        /// <summary>
        /// General log write used by ErrorHandler and elsewhere.
        /// TODO: Replace the stub body with your actual DB INSERT.
        /// </summary>
        public async Task LogAsync(
            DateTime whenUtc,
            LogSeverity severity,
            string category,
            string message,
            string? relatedFile = null,
            string? exType = null,
            string? exMessage = null,
            string? exStack = null,
            string? contentHashHex = null,
            string source = "app")
        {
            // ---------------------------
            // TODO: IMPLEMENT YOUR INSERT
            // ---------------------------
            // Example outline (pseudo-code):
            //
            // using var cmd = _conn.CreateCommand();
            // cmd.CommandText = @"
            //   INSERT INTO Logs
            //   (WhenUtc, Level, Category, Message, RelatedFile, ExType, ExMessage, ExStack, Source, ContentHash)
            //   VALUES (@w,@lvl,@cat,@msg,@file,@ext,@exm,@exs,@src,@hash);";
            // AddParam(cmd, "@w",    whenUtc);
            // AddParam(cmd, "@lvl",  severity.ToShortTag()); // or (int)severity
            // AddParam(cmd, "@cat",  category ?? "");
            // AddParam(cmd, "@msg",  message  ?? "");
            // AddParam(cmd, "@file", relatedFile);
            // AddParam(cmd, "@ext",  exType);
            // AddParam(cmd, "@exm",  exMessage);
            // AddParam(cmd, "@exs",  exStack);
            // AddParam(cmd, "@src",  source);
            // AddParam(cmd, "@hash", contentHashHex);
            // await cmd.ExecuteNonQueryAsync();
            //
            // Keep as no-op for now so you can compile & run:
            await Task.CompletedTask;
        }

        /// <summary>
        /// Used by EarlyLogIngestor; routes to LogAsync with Source='early'.
        /// </summary>
        public Task InsertEarlyFailureAsync(
            DateTime whenUtc,
            string category,
            string message,
            string? relatedFile,
            string? exType,
            string? exMessage,
            string? exStack,
            string contentHashHex)
        {
            return LogAsync(
                whenUtc: whenUtc,
                severity: LogSeverity.Error,   // choose your desired level
                category: category,
                message: message,
                relatedFile: relatedFile,
                exType: exType,
                exMessage: exMessage,
                exStack: exStack,
                contentHashHex: contentHashHex,
                source: "early");
        }
    }
}
