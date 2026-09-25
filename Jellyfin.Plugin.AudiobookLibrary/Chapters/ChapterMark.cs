namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// A chapter start inside one file.
/// </summary>
/// <param name="Name">The embedded chapter name, if any.</param>
/// <param name="StartTicks">Where the chapter starts in its file.</param>
public sealed record ChapterMark(string? Name, long StartTicks);
