using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We run ffmpeg or ffprobe to the end and collect what it printed.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Runs a program and waits for it.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">Its arguments, one per entry so paths with spaces need no quoting.</param>
    /// <param name="cancellationToken">Kills the program.</param>
    /// <returns>The exit code and everything it printed.</returns>
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var registration = cancellationToken.Register(() => Kill(process));

        // Both streams are read as they come, a full pipe would make the program stop and wait for us
        var output = ReadLinesAsync(process.StandardOutput);
        var errors = ReadLinesAsync(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var result = new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<List<string>> ReadLinesAsync(StreamReader reader)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (System.InvalidOperationException)
        {
            // It already finished
        }
    }
}
