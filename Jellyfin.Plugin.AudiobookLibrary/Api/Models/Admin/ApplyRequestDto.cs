using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// Chapters to write into a file, as they stand in the page's preview.
/// </summary>
/// <param name="Chapters">The chapters, the first at 0.</param>
/// <param name="Source">Audible or Silence.</param>
public sealed record ApplyRequestDto(IReadOnlyList<ChapterEntry>? Chapters, string? Source);
