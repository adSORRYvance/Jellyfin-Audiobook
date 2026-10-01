using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// We run Jellyfin's own bundled ffmpeg with the silencedetect filter over a whole file and read what it reports.
/// A full-length book takes a couple of minutes, which is why this only ever runs inside a background job.
/// </summary>
public sealed partial class FfmpegSilenceScanner : ISilenceScanner
{
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<FfmpegSilenceScanner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegSilenceScanner"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Knows where Jellyfin's ffmpeg is.</param>
    /// <param name="logger">Logger.</param>
    public FfmpegSilenceScanner(IMediaEncoder mediaEncoder, ILogger<FfmpegSilenceScanner> logger)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    /// <summary>
    /// Builds ffmpeg's arguments for a scan.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="noiseDb">Anything quieter than this counts as silence.</param>
    /// <returns>The arguments, one per entry so paths with spaces need no quoting.</returns>
    public static IReadOnlyList<string> Arguments(string path, int noiseDb) =>
    [
        "-hide_banner",
        "-nostats",
        "-nostdin",
        "-i",
        path,

        // Cover art shows up as a video stream, and we only want the sound
        "-vn",
        "-af",
        string.Create(CultureInfo.InvariantCulture, $"silencedetect=noise={noiseDb}dB:d=1"),

        // Progress goes to stdout as key=value lines, the silences go to stderr
        "-progress",
        "pipe:1",
        "-f",
        "null",
        "-"
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Silence>> ScanAsync(string path, int noiseDb, Action<double> onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onProgress);

        var startInfo = new ProcessStartInfo(_mediaEncoder.EncoderPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in Arguments(path, noiseDb))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        LowerPriority(process);
        LogStarted(path, noiseDb);

        // Cancelling kills ffmpeg rather than just giving up on it, or it would keep decoding the book in the background
        using var registration = cancellationToken.Register(() => Kill(process));

        // Both streams are read as they come, a full pipe would make ffmpeg stop and wait for us
        var progress = ReadProgressAsync(process.StandardOutput, onProgress);
        var stderr = ReadLinesAsync(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var lines = await stderr.ConfigureAwait(false);
        await progress.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"ffmpeg stopped with exit code {process.ExitCode}: {LastError(lines)}"));
        }

        return SilenceParser.Parse(lines);
    }

    private static async Task ReadProgressAsync(StreamReader reader, Action<double> onProgress)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            // out_time_us is how far into the file ffmpeg has got, it's N/A until the first packet
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
            {
                onProgress(micros / 1_000_000.0);
            }
        }
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

    private static string LastError(List<string> lines)
        => lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("silencedetect", StringComparison.Ordinal))?.Trim()
            ?? "no error message";

    private void LowerPriority(Process process)
    {
        // Playback transcodes matter more than a preview someone is waiting a minute for
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            LogPriorityFailed(ex);
        }
    }

    private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It already finished, which is what we wanted anyway
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scanning {Path} for silences quieter than {NoiseDb} dB")]
    private partial void LogStarted(string path, int noiseDb);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not lower ffmpeg's priority for the silence scan")]
    private partial void LogPriorityFailed(Exception ex);
}
