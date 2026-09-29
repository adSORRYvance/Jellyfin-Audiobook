namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// A user's settings for one book.
/// </summary>
/// <param name="Speed">Playback speed, 1.0 until the user picks another.</param>
public sealed record BookPreferencesDto(double Speed);
