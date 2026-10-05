using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// How Audible's book compares with a file, and Audible's chapters ready to write into it.
/// </summary>
/// <param name="AudibleSec">Audible's runtime.</param>
/// <param name="FileSec">The file's length.</param>
/// <param name="DifferenceSec">The file's length minus Audible's.</param>
/// <param name="Matches">Whether they're close enough that the chapters will land in the right places.</param>
/// <param name="Chapters">Audible's chapters, starting at 0.</param>
/// <param name="DroppedPastEnd">Chapters left out because they start after the file ends.</param>
public sealed record AudibleMatchResult(
    double AudibleSec,
    double FileSec,
    double DifferenceSec,
    bool Matches,
    IReadOnlyList<ChapterEntry> Chapters,
    int DroppedPastEnd);
