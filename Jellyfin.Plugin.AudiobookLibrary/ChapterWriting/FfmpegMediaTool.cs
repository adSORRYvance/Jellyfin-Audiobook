using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We use Jellyfin's own bundled ffmpeg and ffprobe to read, hash and rewrite audiobook files.
/// </summary>
public sealed class FfmpegMediaTool : IMediaTool
{
    private readonly IMediaEncoder _mediaEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegMediaTool"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Knows where Jellyfin's ffmpeg and ffprobe are.</param>
    public FfmpegMediaTool(IMediaEncoder mediaEncoder)
    {
        _mediaEncoder = mediaEncoder;
    }

    /// <summary>
    /// Builds ffmpeg's arguments for writing chapters.
    /// </summary>
    /// <param name="sourcePath">The original file.</param>
    /// <param name="chapterFilePath">The FFMETADATA chapter list.</param>
    /// <param name="destinationPath">Where the copy goes.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> WriteArguments(string sourcePath, string chapterFilePath, string destinationPath) =>
    [
        "-hide_banner",
        "-v",
        "error",
        "-nostdin",
        "-y",
        "-i",
        sourcePath,
        "-i",
        chapterFilePath,

        // Every audio stream and the cover art, the old chapter track is left behind and rebuilt from ours
        "-map",
        "0:a",
        "-map",
        "0:v?",

        // Tags from the original, chapters from our list, map_metadata 1 would wipe the title and author
        "-map_metadata",
        "0",
        "-map_chapters",
        "1",
        "-c",
        "copy",

        // Named because the temp file's extension isn't one ffmpeg knows
        "-f",
        "ipod",
        destinationPath
    ];

    /// <inheritdoc />
    public async Task<MediaFacts> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            _mediaEncoder.ProbePath,
            ["-v", "error", "-show_entries", "format=duration:format_tags", "-show_chapters", "-of", "json", path],
            cancellationToken).ConfigureAwait(false);
        ThrowIfFailed("ffprobe", result);

        using var doc = JsonDocument.Parse(string.Join('\n', result.Output));
        var format = doc.RootElement.GetProperty("format");
        var duration = double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (format.TryGetProperty("tags", out var tagElement))
        {
            foreach (var tag in tagElement.EnumerateObject())
            {
                tags[tag.Name.ToLowerInvariant()] = tag.Value.GetString() ?? string.Empty;
            }
        }

        var chapters = new List<ChapterEntry>();
        if (doc.RootElement.TryGetProperty("chapters", out var chapterElements))
        {
            foreach (var chapter in chapterElements.EnumerateArray())
            {
                var title = chapter.TryGetProperty("tags", out var chapterTags) && chapterTags.TryGetProperty("title", out var t)
                    ? t.GetString() ?? string.Empty
                    : string.Empty;
                chapters.Add(new ChapterEntry(title, double.Parse(chapter.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture)));
            }
        }

        return new MediaFacts(duration, tags, chapters);
    }

    /// <inheritdoc />
    public async Task<string> AudioHashAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            _mediaEncoder.EncoderPath,
            ["-hide_banner", "-v", "error", "-nostdin", "-i", path, "-map", "0:a", "-c", "copy", "-f", "md5", "-"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfFailed("ffmpeg", result);

        return result.Output.FirstOrDefault(l => l.StartsWith("MD5=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("ffmpeg didn't print an audio hash");
    }

    /// <inheritdoc />
    public async Task WriteChaptersAsync(string sourcePath, string chapterFilePath, string destinationPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            _mediaEncoder.EncoderPath,
            WriteArguments(sourcePath, chapterFilePath, destinationPath),
            cancellationToken).ConfigureAwait(false);
        ThrowIfFailed("ffmpeg", result);
    }

    private static void ThrowIfFailed(string program, ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            var message = result.Errors.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "no error message";
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{program} stopped with exit code {result.ExitCode}: {message}"));
        }
    }
}
