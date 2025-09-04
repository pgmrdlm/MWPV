namespace MWPV.Data.Abstractions;

/// <summary>
/// Structured logging contract for the Data layer.
/// Implementations can route to console, encrypted logs, etc.
/// </summary>
public interface IDataLogSink
{
    /// <summary>Informational event.</summary>
    void Info(string evt, object? meta = null);

    /// <summary>Warning event (recoverable issue).</summary>
    void Warn(string evt, object? meta = null, Exception? ex = null);

    /// <summary>Error event (operation failed).</summary>
    void Error(string evt, object? meta = null, Exception? ex = null);
}
