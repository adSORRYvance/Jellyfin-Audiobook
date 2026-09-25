namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// A file in book order, with its playlist title if the .m3u8 had one.
/// </summary>
/// <param name="Source">The file.</param>
/// <param name="Title">The playlist title, or null.</param>
public sealed record OrderedTrack(TrackSource Source, string? Title);
