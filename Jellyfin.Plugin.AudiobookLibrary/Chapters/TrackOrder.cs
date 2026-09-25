using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// We put a book's files in listening order. No single rule works for every book (TM-216),
/// so we try the .m3u8 first, then track tags, then file names.
/// </summary>
public static class TrackOrder
{
    /// <summary>
    /// Orders the files of one book.
    /// </summary>
    /// <param name="tracks">The book's files in any order.</param>
    /// <param name="playlist">The parsed .m3u8, or null when the folder has none.</param>
    /// <returns>The files in listening order.</returns>
    public static IReadOnlyList<OrderedTrack> Sort(IReadOnlyList<TrackSource> tracks, IReadOnlyList<PlaylistEntry>? playlist)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var fallback = SortWithoutPlaylist(tracks);
        if (playlist is null || playlist.Count == 0)
        {
            return fallback.Select(t => new OrderedTrack(t, null)).ToList();
        }

        var result = new List<OrderedTrack>();
        var used = new HashSet<TrackSource>();

        foreach (var entry in playlist)
        {
            var match = FindByPlaylistPath(fallback, used, entry.Path);
            if (match is not null)
            {
                used.Add(match);
                result.Add(new OrderedTrack(match, entry.Title));
            }
        }

        // Files the playlist forgot go at the end, so an old playlist can't hide audio
        result.AddRange(fallback.Where(t => !used.Contains(t)).Select(t => new OrderedTrack(t, null)));
        return result;
    }

    private static List<TrackSource> SortWithoutPlaylist(IReadOnlyList<TrackSource> tracks)
    {
        // GraphicAudio tags restart in every part, so tags only count when they are exactly 1 to N
        var numbers = tracks.Select(t => t.IndexNumber).ToList();
        var cleanTags = numbers.All(n => n.HasValue)
            && numbers.Select(n => n!.Value).Order().SequenceEqual(Enumerable.Range(1, tracks.Count));

        return cleanTags
            ? tracks.OrderBy(t => t.IndexNumber).ToList()
            : tracks.OrderBy(t => t.FileName, NaturalStringComparer.Instance).ToList();
    }

    private static TrackSource? FindByPlaylistPath(List<TrackSource> tracks, HashSet<TrackSource> used, string path)
    {
        var name = M3u8Parser.FileNameOf(path);
        var match = tracks.Find(t => !used.Contains(t) && t.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // Some tools write %20 for spaces, try the decoded name when the plain one misses
        var decoded = M3u8Parser.FileNameOf(Uri.UnescapeDataString(path));
        return tracks.Find(t => !used.Contains(t) && t.FileName.Equals(decoded, StringComparison.OrdinalIgnoreCase));
    }
}
