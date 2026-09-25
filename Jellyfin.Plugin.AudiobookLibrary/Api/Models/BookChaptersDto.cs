using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// A book's timeline as the player sees it.
/// </summary>
/// <param name="DurationSec">The whole book's length.</param>
/// <param name="Tracks">The files in listening order.</param>
/// <param name="Chapters">The chapters in listening order.</param>
public sealed record BookChaptersDto(double DurationSec, IReadOnlyList<BookTrackDto> Tracks, IReadOnlyList<BookChapterDto> Chapters);
