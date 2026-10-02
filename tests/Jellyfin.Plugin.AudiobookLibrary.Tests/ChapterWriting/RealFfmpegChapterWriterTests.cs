using System.Diagnostics;
using System.Text.Json;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.ChapterWriting;

// Rewrites a generated M4B with the real ffmpeg, so it only runs when asked and FFMPEG and FFPROBE point at binaries
// FFMPEG=/path/ffmpeg FFPROBE=/path/ffprobe dotnet run --project tests/Jellyfin.Plugin.AudiobookLibrary.Tests -c Release -- -explicit only
public sealed class RealFfmpegChapterWriterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-realwrite-" + Guid.NewGuid().ToString("N"));

    public RealFfmpegChapterWriterTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact(Explicit = true)]
    public async Task RealFfmpeg_WritesChapters_KeepsTagsCoverAndAudio_AndRestores()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG");
        var ffprobe = Environment.GetEnvironmentVariable("FFPROBE");
        Assert.False(string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(ffprobe), "set FFMPEG and FFPROBE");
        var token = TestContext.Current.CancellationToken;

        // 60 s of tone with a cover, tags and one placeholder chapter, like the books Audible doesn't match
        var book = Path.Combine(_folder, "Test Book.m4b");
        var meta = Path.Combine(_folder, "placeholder.txt");
        await File.WriteAllTextAsync(meta, ";FFMETADATA1\ntitle=Test Book\nartist=Test Author\nalbum=Test Book\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=60000\ntitle=Chapter_0\n", token);
        await Run(ffmpeg!, token,
            "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=60",
            // One frame of cover, -frames:v 1 would end the whole output after that frame
            "-f", "lavfi", "-i", "color=c=red:s=64x64:d=0.04", "-i", meta,
            "-map", "0:a", "-map", "1:v", "-map_metadata", "2", "-map_chapters", "2",
            "-c:a", "aac", "-c:v", "mjpeg", "-disposition:v", "attached_pic", "-f", "ipod", book);

        var tool = new FfmpegMediaTool(MediaEncoderPaths.Create(ffmpeg!, ffprobe!));
        var writer = new M4bChapterWriter(tool, NullLogger<M4bChapterWriter>.Instance);
        var originalHash = await tool.AudioHashAsync(book, token);

        await writer.WriteAsync(book, [new ChapterEntry("Part One; The Start", 0), new ChapterEntry("Épilogue", 42.5)], _ => { }, token);

        var after = await tool.ProbeAsync(book, token);
        Assert.Equal([new ChapterEntry("Part One; The Start", 0), new ChapterEntry("Épilogue", 42.5)], after.Chapters);
        Assert.Equal("Test Book", after.Tags["title"]);
        Assert.Equal("Test Author", after.Tags["artist"]);
        Assert.Equal(originalHash, await tool.AudioHashAsync(book, token));
        Assert.True(await HasCover(ffprobe!, book, token));
        Assert.True(File.Exists(M4bChapterWriter.BackupPath(book)));

        writer.Restore(book);

        Assert.Equal("Chapter_0", Assert.Single((await tool.ProbeAsync(book, token)).Chapters).Title);
        Assert.False(File.Exists(M4bChapterWriter.BackupPath(book)));
    }

    private static async Task Run(string program, CancellationToken token, params string[] args)
    {
        using var process = Process.Start(program, args);
        await process.WaitForExitAsync(token);
        Assert.Equal(0, process.ExitCode);
    }

    private static async Task<bool> HasCover(string ffprobe, string path, CancellationToken token)
    {
        var start = new ProcessStartInfo(ffprobe) { RedirectStandardOutput = true };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "stream=codec_type:stream_disposition=attached_pic", "-of", "json", path })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var json = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("streams").EnumerateArray()
            .Any(s => s.GetProperty("codec_type").GetString() == "video" && s.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 1);
    }
}
