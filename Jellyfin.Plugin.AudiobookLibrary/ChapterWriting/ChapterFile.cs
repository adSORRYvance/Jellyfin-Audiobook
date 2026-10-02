using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We turn a chapter list into ffmpeg's FFMETADATA format, and refuse a list that can't be right before ffmpeg ever sees it.
/// </summary>
public static class ChapterFile
{
    /// <summary>
    /// Checks a chapter list against the file it's meant for.
    /// </summary>
    /// <param name="chapters">The chapters.</param>
    /// <param name="durationSec">The file's length.</param>
    /// <returns>Why the list can't be written, or null when it's fine.</returns>
    public static string? Validate(IReadOnlyList<ChapterEntry>? chapters, double durationSec)
    {
        if (chapters is null || chapters.Count == 0)
        {
            return "The chapter list is empty";
        }

        // Players treat the first chapter's start as the start of the book, a gap before it would hide the opening
        if (chapters[0].StartSec != 0)
        {
            return "The first chapter must start at 0";
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            if (string.IsNullOrWhiteSpace(chapter.Title))
            {
                return string.Create(CultureInfo.InvariantCulture, $"Chapter {i + 1} has no title");
            }

            if (i > 0 && chapter.StartSec <= chapters[i - 1].StartSec)
            {
                return string.Create(CultureInfo.InvariantCulture, $"Chapter {i + 1} doesn't start after chapter {i}");
            }

            if (chapter.StartSec >= durationSec)
            {
                return string.Create(CultureInfo.InvariantCulture, $"Chapter {i + 1} starts after the end of the file");
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the FFMETADATA text for a list that passed <see cref="Validate"/>.
    /// </summary>
    /// <param name="chapters">The chapters.</param>
    /// <param name="durationSec">The file's length, where the last chapter ends.</param>
    /// <returns>The text to hand ffmpeg as its second input.</returns>
    public static string Build(IReadOnlyList<ChapterEntry> chapters, double durationSec)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        var text = new StringBuilder(";FFMETADATA1\n");
        for (var i = 0; i < chapters.Count; i++)
        {
            // Each chapter ends where the next begins, so there are no gaps for a player to fall into
            var end = i + 1 < chapters.Count ? chapters[i + 1].StartSec : durationSec;
            text.Append("[CHAPTER]\nTIMEBASE=1/1000\n")
                .Append(CultureInfo.InvariantCulture, $"START={Millis(chapters[i].StartSec)}\n")
                .Append(CultureInfo.InvariantCulture, $"END={Millis(end)}\n")
                .Append("title=").Append(Escape(chapters[i].Title.Trim())).Append('\n');
        }

        return text.ToString();
    }

    private static long Millis(double seconds) => (long)Math.Round(seconds * 1000);

    // These characters mean something in FFMETADATA, so a title like "Part One; The Beginning" needs them escaped
    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '=' or ';' or '#' or '\\' or '\n')
            {
                escaped.Append('\\');
            }

            if (c != '\r')
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }
}
