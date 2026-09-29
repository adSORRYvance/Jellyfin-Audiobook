namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// An author or narrator as Audible lists them on a book.
/// </summary>
/// <param name="Asin">The author's Audible id, which narrators and some authors don't have.</param>
/// <param name="Name">The name as shown on Audible.</param>
public sealed record AudiblePerson(string? Asin, string Name);
