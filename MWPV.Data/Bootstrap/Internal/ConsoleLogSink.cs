using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

/// <summary>
/// Temporary logger that writes to the console. 
/// Replace with your encrypted logger when ready.
/// </summary>
internal sealed class ConsoleLogSink : IDataLogSink
{
    public void Info(string evt, object? meta = null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[INFO ] {evt} {FormatMeta(meta)}");
        Console.ResetColor();
    }

    public void Warn(string evt, object? meta = null, Exception? ex = null)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN ] {evt} {FormatMeta(meta)} {FormatException(ex)}");
        Console.ResetColor();
    }

    public void Error(string evt, object? meta = null, Exception? ex = null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {evt} {FormatMeta(meta)} {FormatException(ex)}");
        Console.ResetColor();
    }

    private static string FormatMeta(object? meta) =>
        meta is null ? string.Empty : $"| meta: {meta}";

    private static string FormatException(Exception? ex) =>
        ex is null ? string.Empty : $"| ex: {ex.GetType().Name} - {ex.Message}";
}
