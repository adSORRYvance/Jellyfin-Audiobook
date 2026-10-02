using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// What ffprobe says about a file, the parts we compare before and after writing chapters.
/// </summary>
/// <param name="DurationSec">The file's length.</param>
/// <param name="Tags">The file-level tags, like title and artist, with lower-case keys.</param>
/// <param name="Chapters">The chapters in the file.</param>
public sealed record MediaFacts(double DurationSec, IReadOnlyDictionary<string, string> Tags, IReadOnlyList<ChapterEntry> Chapters);
