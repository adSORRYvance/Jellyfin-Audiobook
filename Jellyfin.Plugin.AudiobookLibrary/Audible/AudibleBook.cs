using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// A book's details from Audnexus, with the field names Audnexus uses.
/// </summary>
/// <param name="Asin">The book's Audible id in <paramref name="Region"/>.</param>
/// <param name="Title">The title.</param>
/// <param name="Subtitle">The subtitle, like "A Mistborn Novel".</param>
/// <param name="Authors">The authors, most with their own Audible id.</param>
/// <param name="Narrators">The narrators.</param>
/// <param name="SeriesPrimary">The main series and the book's position in it.</param>
/// <param name="SeriesSecondary">A wider series, like "The Cosmere", usually without a position.</param>
/// <param name="Summary">The publisher's summary as HTML.</param>
/// <param name="Description">The same summary as plain text.</param>
/// <param name="Image">The cover image URL.</param>
/// <param name="RuntimeLengthMin">The book's length in whole minutes.</param>
/// <param name="ReleaseDate">The release date as Audnexus sends it.</param>
/// <param name="Language">The book's language, like "english".</param>
/// <param name="Region">The Audible store this came from.</param>
public sealed record AudibleBook(
    string Asin,
    string Title,
    string? Subtitle,
    IReadOnlyList<AudiblePerson>? Authors,
    IReadOnlyList<AudiblePerson>? Narrators,
    AudibleSeries? SeriesPrimary,
    AudibleSeries? SeriesSecondary,
    string? Summary,
    string? Description,
    string? Image,
    int? RuntimeLengthMin,
    string? ReleaseDate,
    string? Language,
    string Region);
