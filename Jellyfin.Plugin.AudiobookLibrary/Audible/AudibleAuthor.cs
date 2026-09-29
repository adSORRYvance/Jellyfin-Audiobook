namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// An author's page from Audnexus.
/// </summary>
/// <param name="Asin">The author's Audible id.</param>
/// <param name="Name">The author's name.</param>
/// <param name="Description">The author's bio.</param>
/// <param name="Image">The author photo URL.</param>
/// <param name="Region">The Audible store this came from.</param>
public sealed record AudibleAuthor(string Asin, string Name, string? Description, string? Image, string Region);
