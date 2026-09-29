namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// One chapter as Audible has it.
/// </summary>
/// <param name="Title">The chapter title.</param>
/// <param name="StartOffsetMs">Where the chapter starts, from the start of the book.</param>
/// <param name="LengthMs">How long the chapter is.</param>
public sealed record AudibleChapter(string Title, long StartOffsetMs, long LengthMs);
