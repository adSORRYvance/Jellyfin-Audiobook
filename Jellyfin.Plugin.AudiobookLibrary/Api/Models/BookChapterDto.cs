namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// One chapter on the book timeline.
/// </summary>
/// <param name="Title">The chapter title.</param>
/// <param name="StartSec">Where the chapter starts in the book.</param>
/// <param name="EndSec">Where it ends in the book.</param>
/// <param name="TrackIndex">The file the chapter starts in.</param>
/// <param name="TrackOffsetSec">Where the chapter starts inside that file.</param>
public sealed record BookChapterDto(string Title, double StartSec, double EndSec, int TrackIndex, double TrackOffsetSec);
