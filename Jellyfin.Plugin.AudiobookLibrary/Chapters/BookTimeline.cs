using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// We lay a book's files end to end on one timeline and place every chapter on it.
/// Everything stays in ticks here, the controller turns them into seconds for the browser.
/// </summary>
public static class BookTimeline
{
    /// <summary>
    /// Builds the whole-book timeline from files already in listening order.
    /// </summary>
    /// <param name="tracks">The files in listening order.</param>
    /// <returns>The tracks and chapters on one timeline.</returns>
    public static BookLayout Build(IReadOnlyList<OrderedTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var layoutTracks = new List<BookTrack>();
        var starts = new List<(string Title, long Start, int TrackIndex, long Offset)>();
        long bookStart = 0;

        for (var index = 0; index < tracks.Count; index++)
        {
            var source = tracks[index].Source;
            var length = Math.Max(0, source.RunTimeTicks);
            layoutTracks.Add(new BookTrack(source.ItemId, bookStart, length));

            var marks = UsableChapters(source);
            if (marks.Count > 0)
            {
                foreach (var mark in marks)
                {
                    var name = string.IsNullOrWhiteSpace(mark.Name) ? $"Chapter {starts.Count + 1}" : mark.Name.Trim();
                    starts.Add((name, bookStart + mark.StartTicks, index, mark.StartTicks));
                }
            }
            else if (tracks.Count > 1)
            {
                // A file without chapters in a multi-file book is usually one chapter, like "Chapter 3.mp3"
                starts.Add((TrackTitle(tracks[index]), bookStart, index, 0));
            }

            bookStart += length;
        }

        var chapters = new List<BookChapter>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Start : bookStart;
            chapters.Add(new BookChapter(starts[i].Title, starts[i].Start, end, starts[i].TrackIndex, starts[i].Offset));
        }

        return new BookLayout(bookStart, layoutTracks, chapters);
    }

    /// <summary>
    /// Checks whether a file's embedded chapters mean anything.
    /// </summary>
    /// <param name="chapters">The chapters Jellyfin found in the file.</param>
    /// <returns>False for no chapters or a lone placeholder at 0 s.</returns>
    public static bool HasRealChapters(IReadOnlyList<ChapterMark> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        // Many rips carry a single "Chapter_0" at 0 s, which tells the listener nothing
        return chapters.Count > 1 || (chapters.Count == 1 && chapters[0].StartTicks > 0);
    }

    private static List<ChapterMark> UsableChapters(TrackSource source)
    {
        if (!HasRealChapters(source.Chapters))
        {
            return [];
        }

        // Drop marks outside the file and repeats at the same spot, those would make empty or backwards chapters
        return source.Chapters
            .Where(c => c.StartTicks >= 0 && (source.RunTimeTicks <= 0 || c.StartTicks < source.RunTimeTicks))
            .OrderBy(c => c.StartTicks)
            .DistinctBy(c => c.StartTicks)
            .ToList();
    }

    private static string TrackTitle(OrderedTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Title))
        {
            return track.Title;
        }

        var name = Path.GetFileNameWithoutExtension(track.Source.FileName);
        return string.IsNullOrWhiteSpace(name) ? track.Source.FileName : name;
    }
}
