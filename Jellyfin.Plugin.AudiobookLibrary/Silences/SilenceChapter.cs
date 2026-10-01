namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// A chapter suggested from the silences, only a preview until TM-224 writes it into the file.
/// </summary>
/// <param name="Title">"Chapter N", since silence can't tell us a name.</param>
/// <param name="StartSec">Where the chapter starts in the file.</param>
public sealed record SilenceChapter(string Title, double StartSec);
