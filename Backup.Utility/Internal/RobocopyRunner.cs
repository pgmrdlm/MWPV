using System.Diagnostics;
using System.Text;

namespace Backup.Utility.Internal;

internal sealed record RobocopyRunResult(int ExitCode);

internal interface IRobocopyRunner
{
    Task<RobocopyRunResult> RunAsync(string executable, string source, string destination, CancellationToken cancellationToken);
}

internal sealed class RobocopyRunner : IRobocopyRunner
{
    private const int MaximumCapturedCharacters = 64 * 1024;

    public async Task<RobocopyRunResult> RunAsync(
        string executable,
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add(source);
        startInfo.ArgumentList.Add(destination);
        foreach (string argument in new[] { "/E", "/COPY:DAT", "/DCOPY:DAT", "/Z", "/R:2", "/W:2", "/XJ", "/NP" })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Robocopy did not start.");

        var output = new StringBuilder();
        Task stdout = DrainBoundedAsync(process.StandardOutput, output);
        Task stderr = DrainBoundedAsync(process.StandardError, output);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new RobocopyRunResult(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }

            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static async Task DrainBoundedAsync(StreamReader reader, StringBuilder capture)
    {
        char[] buffer = new char[2048];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) is int count && count > 0)
        {
            lock (capture)
            {
                int remaining = MaximumCapturedCharacters - capture.Length;
                if (remaining > 0)
                    capture.Append(buffer, 0, Math.Min(count, remaining));
            }
        }
    }
}
