using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// A book's chapters from Audnexus.
/// The runtime is what lets us tell whether a file is the same edition, a matching ASIN lines up to within a second.
/// </summary>
/// <param name="Asin">The book's Audible id.</param>
/// <param name="IsAccurate">Audible's own flag for whether the chapter times were checked.</param>
/// <param name="RuntimeLengthMs">The whole book's length.</param>
/// <param name="BrandIntroDurationMs">Length of the "This is Audible" intro at the start.</param>
/// <param name="BrandOutroDurationMs">Length of the Audible outro at the end.</param>
/// <param name="Chapters">The chapters in order.</param>
/// <param name="Region">The Audible store this came from.</param>
public sealed record AudibleChapters(
    string Asin,
    bool IsAccurate,
    long RuntimeLengthMs,
    long BrandIntroDurationMs,
    long BrandOutroDurationMs,
    IReadOnlyList<AudibleChapter>? Chapters,
    string Region);
