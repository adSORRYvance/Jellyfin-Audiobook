namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// An ASIN to save for a file.
/// </summary>
/// <param name="Asin">The ASIN, or empty to clear it.</param>
/// <param name="Region">The Audible store, the plugin setting when left out.</param>
public sealed record AsinRequestDto(string? Asin, string? Region);
