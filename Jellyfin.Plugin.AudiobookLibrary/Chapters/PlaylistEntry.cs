namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// One file line from an .m3u8, with the title from the #EXTINF line above it if there was one.
/// </summary>
/// <param name="Path">The path exactly as the playlist wrote it.</param>
/// <param name="Title">The title with any shared author prefix removed, or null.</param>
public sealed record PlaylistEntry(string Path, string? Title);
