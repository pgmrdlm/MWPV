namespace MWPV.Data.Abstractions;

public interface IDataLogSink
{
    void Info(string evt, object? meta = null);
    void Warn(string evt, object? meta = null, Exception? ex = null);
    void Error(string evt, object? meta = null, Exception? ex = null);
}
