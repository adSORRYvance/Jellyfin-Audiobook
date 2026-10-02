using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We write chapters into an M4B by making a copy next to it, proving the copy has the same sound and tags, and only then swapping it in.
/// The first time, the original is kept as name.bak and never overwritten after that, so Restore always gets back the file as it was.
/// </summary>
public sealed partial class M4bChapterWriter
{
    /// <summary>
    /// The file types we can write chapters into.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".m4b", ".m4a"];

    // Tags Jellyfin uses to name the book, they have to come through the rewrite unchanged
    private static readonly string[] _keptTags = ["title", "artist", "album", "album_artist"];

    private readonly IMediaTool _tool;
    private readonly ILogger<M4bChapterWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="M4bChapterWriter"/> class.
    /// </summary>
    /// <param name="tool">Reads, hashes and rewrites the files.</param>
    /// <param name="logger">Logger.</param>
    public M4bChapterWriter(IMediaTool tool, ILogger<M4bChapterWriter> logger)
    {
        _tool = tool;
        _logger = logger;
    }

    /// <summary>
    /// Gets where the original is kept.
    /// </summary>
    /// <param name="path">The audiobook file.</param>
    /// <returns>The backup's path.</returns>
    public static string BackupPath(string path) => path + ".bak";

    /// <summary>
    /// Gets where the copy is written, hidden and with an extension Jellyfin won't take for a new audiobook.
    /// </summary>
    /// <param name="path">The audiobook file.</param>
    /// <returns>The temp file's path.</returns>
    public static string TempPath(string path)
        => Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "." + Path.GetFileName(path) + ".abl-tmp");

    /// <summary>
    /// Checks a file is one we can write chapters into.
    /// </summary>
    /// <param name="path">The audiobook file.</param>
    /// <returns>True for M4B and M4A files.</returns>
    public static bool CanWrite(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes chapters into a file, or leaves it untouched and throws saying which check failed.
    /// </summary>
    /// <param name="path">The audiobook file.</param>
    /// <param name="chapters">The chapters, starting at 0.</param>
    /// <param name="onStage">Told about each step, for whoever is polling.</param>
    /// <param name="cancellationToken">Stops the write, but never once the swap has begun.</param>
    /// <returns>A task that finishes once the new file is in place.</returns>
    public async Task WriteAsync(string path, IReadOnlyList<ChapterEntry> chapters, Action<ChapterWriteStage> onStage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(onStage);

        onStage(ChapterWriteStage.Preparing);
        if (!CanWrite(path))
        {
            throw new InvalidOperationException("Only .m4b and .m4a files can get chapters");
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{path} doesn't exist"));
        }

        var folder = file.DirectoryName!;
        CheckWritable(folder);
        CheckFreeSpace(folder, file.Length);

        var before = await _tool.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
        var problem = ChapterFile.Validate(chapters, before.DurationSec);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        var beforeHash = await _tool.AudioHashAsync(path, cancellationToken).ConfigureAwait(false);

        var temp = TempPath(path);
        var chapterFile = Path.Combine(Path.GetTempPath(), $"abl-chapters-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(chapterFile, ChapterFile.Build(chapters, before.DurationSec), cancellationToken).ConfigureAwait(false);

            onStage(ChapterWriteStage.Writing);
            File.Delete(temp);
            await _tool.WriteChaptersAsync(path, chapterFile, temp, cancellationToken).ConfigureAwait(false);

            onStage(ChapterWriteStage.Checking);
            var after = await _tool.ProbeAsync(temp, cancellationToken).ConfigureAwait(false);
            var afterHash = await _tool.AudioHashAsync(temp, cancellationToken).ConfigureAwait(false);
            Check(before, beforeHash, after, afterHash, chapters);

            // Last chance to stop, from here the swap runs to the end so the file is never left half-swapped
            cancellationToken.ThrowIfCancellationRequested();
            onStage(ChapterWriteStage.Replacing);
            Swap(path, temp);
            LogWritten(chapters.Count, path);
        }
        finally
        {
            DeleteQuietly(temp);
            DeleteQuietly(chapterFile);
        }
    }

    /// <summary>
    /// Puts the original back from its backup.
    /// </summary>
    /// <param name="path">The audiobook file.</param>
    public void Restore(string path)
    {
        var backup = BackupPath(path);
        if (!File.Exists(backup))
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"There's no backup of {path} to restore"));
        }

        // One rename that replaces the file, so there's never a moment with no file at all
        File.Move(backup, path, true);
        LogRestored(path);
    }

    private static void Check(MediaFacts before, string beforeHash, MediaFacts after, string afterHash, IReadOnlyList<ChapterEntry> chapters)
    {
        if (Math.Abs(after.DurationSec - before.DurationSec) > 0.5)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The new file is {after.DurationSec:F1} s long instead of {before.DurationSec:F1} s, the original is untouched"));
        }

        if (!string.Equals(beforeHash, afterHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The new file's audio doesn't match the original, the original is untouched");
        }

        var chaptersMatch = after.Chapters.Count == chapters.Count
            && after.Chapters.Zip(chapters).All(p => p.First.Title == p.Second.Title.Trim() && Math.Abs(p.First.StartSec - p.Second.StartSec) <= 0.01);
        if (!chaptersMatch)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The new file has {after.Chapters.Count} chapters that don't match the {chapters.Count} asked for, the original is untouched"));
        }

        foreach (var tag in _keptTags)
        {
            if (before.Tags.TryGetValue(tag, out var value) && (!after.Tags.TryGetValue(tag, out var newValue) || newValue != value))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The new file lost its {tag} tag, the original is untouched"));
            }
        }
    }

    private static void Swap(string path, string temp)
    {
        var backup = BackupPath(path);

        // A backup that already exists is the true original from the first write, so it stays as it is
        if (File.Exists(backup))
        {
            File.Move(temp, path, true);
            return;
        }

        File.Move(path, backup);
        try
        {
            File.Move(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original goes straight back, so a failed swap looks like nothing happened
            File.Move(backup, path);
            throw;
        }
    }

    private static void CheckWritable(string folder)
    {
        var probe = Path.Combine(folder, $".abl-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Jellyfin can't write to {folder} ({ex.Message}). The folder must be writable by the user Jellyfin runs as."),
                ex);
        }
    }

    private static void CheckFreeSpace(string folder, long fileSize)
    {
        // The drive holding the folder is the one with the longest mount point that the folder starts with
        var drive = DriveInfo.GetDrives()
            .Where(d => d.IsReady && folder.StartsWith(d.Name, StringComparison.Ordinal))
            .MaxBy(d => d.Name.Length);
        var needed = fileSize + (fileSize / 10);
        if (drive is not null && drive.AvailableFreeSpace < needed)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Not enough free space next to the file, it needs {needed / 1048576} MB and {drive.AvailableFreeSpace / 1048576} MB is free"));
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover temp files are hidden and harmless, and the next write replaces them
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote {Count} chapters into {Path}")]
    private partial void LogWritten(int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restored {Path} from its backup")]
    private partial void LogRestored(string path);
}
