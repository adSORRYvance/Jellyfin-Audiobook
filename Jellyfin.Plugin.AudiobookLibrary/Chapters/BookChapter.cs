namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// One chapter on the book timeline.
/// </summary>
/// <param name="Title">The chapter title.</param>
/// <param name="StartTicks">Where the chapter starts in the book.</param>
/// <param name="EndTicks">Where the next chapter starts, or the end of the book.</param>
/// <param name="TrackIndex">The file the chapter starts in.</param>
/// <param name="TrackOffsetTicks">Where the chapter starts inside that file.</param>
public sealed record BookChapter(string Title, long StartTicks, long EndTicks, int TrackIndex, long TrackOffsetTicks);
