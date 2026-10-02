using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// Chapters to write into a file.
/// </summary>
/// <param name="Chapters">The chapters, the first starting at 0.</param>
public sealed record ChapterWriteRequestDto(IReadOnlyList<ChapterEntry>? Chapters);
