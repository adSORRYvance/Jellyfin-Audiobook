using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// A book laid out on one timeline, in ticks.
/// </summary>
/// <param name="DurationTicks">The whole book's length.</param>
/// <param name="Tracks">The files in listening order.</param>
/// <param name="Chapters">The chapters in listening order, empty when the book has none.</param>
public sealed record BookLayout(long DurationTicks, IReadOnlyList<BookTrack> Tracks, IReadOnlyList<BookChapter> Chapters);
