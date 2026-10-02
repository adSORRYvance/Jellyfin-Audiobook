namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// One chapter to write into a file.
/// </summary>
/// <param name="Title">The chapter's name.</param>
/// <param name="StartSec">Where it starts, from the start of the file.</param>
public sealed record ChapterEntry(string Title, double StartSec);
