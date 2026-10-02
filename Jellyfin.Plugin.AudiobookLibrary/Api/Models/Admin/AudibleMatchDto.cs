using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// Audible's chapters for a file, and how well Audible's book lines up with it.
/// </summary>
/// <param name="Title">Audible's title for the ASIN, when Audnexus has it, so the admin can see it's the right book.</param>
/// <param name="Asin">The ASIN looked up.</param>
/// <param name="Region">The Audible store.</param>
/// <param name="AudibleSec">Audible's runtime.</param>
/// <param name="FileSec">The file's length.</param>
/// <param name="DifferenceSec">The file's length minus Audible's.</param>
/// <param name="Matches">Whether they're within 2 s.</param>
/// <param name="IsOld">True when Audnexus was down and this is an older saved copy.</param>
/// <param name="DroppedPastEnd">Audible chapters left out because they start after the file ends.</param>
/// <param name="Chapters">The chapters, starting at 0.</param>
public sealed record AudibleMatchDto(
    string? Title,
    string Asin,
    string Region,
    double AudibleSec,
    double FileSec,
    double DifferenceSec,
    bool Matches,
    bool IsOld,
    int DroppedPastEnd,
    IReadOnlyList<ChapterEntry> Chapters);
