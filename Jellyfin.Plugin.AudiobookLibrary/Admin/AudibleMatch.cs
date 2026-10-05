using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// We check Audible's book against a file and turn its chapters into ones we can write.
/// A matching ASIN lines up to within a second (The Lost Metal is 0.04 s apart), so a bigger gap means another edition or the wrong book.
/// </summary>
public static class AudibleMatch
{
    /// <summary>
    /// Files further apart than this get a warning, the page asks before applying.
    /// </summary>
    public const double ToleranceSec = 2;

    /// <summary>
    /// Compares Audible's chapters with a file.
    /// </summary>
    /// <param name="audible">What Audnexus returned.</param>
    /// <param name="fileSec">The file's length.</param>
    /// <returns>The comparison and the chapters.</returns>
    public static AudibleMatchResult Compare(AudibleChapters audible, double fileSec)
    {
        ArgumentNullException.ThrowIfNull(audible);

        var audibleSec = audible.RuntimeLengthMs / 1000.0;
        var chapters = new List<ChapterEntry>();
        var dropped = 0;
        foreach (var chapter in audible.Chapters ?? [])
        {
            var start = chapter.StartOffsetMs / 1000.0;

            // A shorter file can't hold Audible's last chapters, writing them would fail the length check
            if (start >= fileSec)
            {
                dropped++;
                continue;
            }

            if (chapters.Count > 0 && start <= chapters[^1].StartSec)
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(chapter.Title)
                ? string.Create(CultureInfo.InvariantCulture, $"Chapter {chapters.Count + 1}")
                : chapter.Title.Trim();

            // Players take the first chapter's start as the book's start, so it's always 0
            chapters.Add(new ChapterEntry(title, chapters.Count == 0 ? 0 : start));
        }

        var difference = Math.Round(fileSec - audibleSec, 2);
        return new AudibleMatchResult(audibleSec, fileSec, difference, Math.Abs(difference) <= ToleranceSec, chapters, dropped);
    }
}
