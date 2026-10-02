using System.Globalization;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.ChapterWriting;

// Stands in for ffmpeg: the "rewrite" writes the chapter list into the copy, and each check can be made to fail
internal sealed class FakeMediaTool : IMediaTool
{
    public double DurationSec { get; set; } = 3600;

    public Dictionary<string, string> Tags { get; } = new() { ["title"] = "The Book", ["artist"] = "The Author", ["album"] = "The Book" };

    public double? CopyDurationSec { get; set; }

    public string? CopyHash { get; set; }

    public bool CopyLosesArtist { get; set; }

    public bool CopyDropsLastChapter { get; set; }

    public TaskCompletionSource? WriteGate { get; set; }

    public Action<string>? AfterCopyHashed { get; set; }

    public List<string> Calls { get; } = [];

    public async Task<MediaFacts> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        Calls.Add("probe " + Path.GetFileName(path));
        if (!IsCopy(path))
        {
            return new MediaFacts(DurationSec, Tags, [new ChapterEntry("Chapter_0", 0)]);
        }

        var tags = new Dictionary<string, string>(Tags);
        if (CopyLosesArtist)
        {
            tags.Remove("artist");
        }

        var chapters = ReadChapters((await File.ReadAllLinesAsync(path, cancellationToken)).Skip(1));
        if (CopyDropsLastChapter)
        {
            chapters.RemoveAt(chapters.Count - 1);
        }

        return new MediaFacts(CopyDurationSec ?? DurationSec, tags, chapters);
    }

    public Task<string> AudioHashAsync(string path, CancellationToken cancellationToken)
    {
        Calls.Add("hash " + Path.GetFileName(path));
        if (IsCopy(path))
        {
            AfterCopyHashed?.Invoke(path);
            return Task.FromResult(CopyHash ?? "MD5=same");
        }

        return Task.FromResult("MD5=same");
    }

    public async Task WriteChaptersAsync(string sourcePath, string chapterFilePath, string destinationPath, CancellationToken cancellationToken)
    {
        Calls.Add("write " + Path.GetFileName(destinationPath));
        if (WriteGate is not null)
        {
            await WriteGate.Task.WaitAsync(cancellationToken);
        }

        // The copy's content is the chapter list, so a test can tell the new file from the original
        await File.WriteAllTextAsync(destinationPath, "new\n" + await File.ReadAllTextAsync(chapterFilePath, cancellationToken), cancellationToken);
    }

    private static bool IsCopy(string path) => path.EndsWith(".abl-tmp", StringComparison.Ordinal);

    private static List<ChapterEntry> ReadChapters(IEnumerable<string> lines)
    {
        var chapters = new List<ChapterEntry>();
        double start = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("START=", StringComparison.Ordinal))
            {
                start = long.Parse(line[6..], CultureInfo.InvariantCulture) / 1000.0;
            }
            else if (line.StartsWith("title=", StringComparison.Ordinal))
            {
                chapters.Add(new ChapterEntry(line[6..].Replace("\\", string.Empty, StringComparison.Ordinal), start));
            }
        }

        return chapters;
    }
}
