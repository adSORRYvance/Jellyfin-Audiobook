namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// A series a book belongs to.
/// </summary>
/// <param name="Asin">The series' Audible id.</param>
/// <param name="Name">The series name.</param>
/// <param name="Position">Where the book sits in the series, kept as text because Audible has positions like "2.5" and "1-3".</param>
public sealed record AudibleSeries(string? Asin, string Name, string? Position);
