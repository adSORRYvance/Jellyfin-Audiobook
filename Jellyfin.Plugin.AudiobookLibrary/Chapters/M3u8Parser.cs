using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// We read the .m3u8 that ships next to MP3 audiobooks for its file order and chapter titles.
/// Jellyfin ignores these playlists in a Books library, so nothing else parses them for us.
/// </summary>
public static class M3u8Parser
{
    private const string PrefixSeparator = " - ";

    /// <summary>
    /// Parses playlist text into its file entries, in playlist order.
    /// </summary>
    /// <param name="text">The whole .m3u8 file.</param>
    /// <returns>The entries, empty if the playlist lists no files.</returns>
    public static IReadOnlyList<PlaylistEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entries = new List<PlaylistEntry>();
        string? pendingTitle = null;

        foreach (var rawLine in text.TrimStart('﻿').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // The duration is rounded to whole seconds, so we only keep the title after the comma
                var comma = line.IndexOf(',', StringComparison.Ordinal);
                var title = comma < 0 ? string.Empty : line[(comma + 1)..].Trim();
                pendingTitle = title.Length == 0 ? null : title;
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            entries.Add(new PlaylistEntry(line, pendingTitle));
            pendingTitle = null;
        }

        return RemoveSharedPrefix(entries);
    }

    /// <summary>
    /// Gets the file name part of a playlist path, whichever slash style the tool that wrote it used.
    /// </summary>
    /// <param name="path">A path from a playlist entry.</param>
    /// <returns>The last segment of the path.</returns>
    public static string FileNameOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? path : path[(slash + 1)..];
    }

    // Titles like "Trang Thanh Tran - Chapter 1" only lose the prefix when every title has the same one
    // Cutting at the first " - " on its own would break a title like "Part 1 - The Beginning"
    private static List<PlaylistEntry> RemoveSharedPrefix(List<PlaylistEntry> entries)
    {
        if (entries.Count < 2 || entries.Any(e => e.Title is null))
        {
            return entries;
        }

        var first = entries[0].Title!;
        var cut = first.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        if (cut <= 0)
        {
            return entries;
        }

        var prefix = first[..(cut + PrefixSeparator.Length)];
        if (entries.Any(e => !e.Title!.StartsWith(prefix, StringComparison.Ordinal) || e.Title.Length == prefix.Length))
        {
            return entries;
        }

        return entries.Select(e => e with { Title = e.Title![prefix.Length..].Trim() }).ToList();
    }
}
