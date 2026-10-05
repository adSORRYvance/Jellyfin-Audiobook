using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// What to write: one file and its new chapters.
/// </summary>
/// <param name="ItemId">The file's AudioBook item, refreshed afterwards.</param>
/// <param name="Path">The file on disk.</param>
/// <param name="Chapters">The chapters to write.</param>
/// <param name="Source">Where the chapters came from, Audible or Silence, kept so the admin page can show it.</param>
public sealed record ChapterWriteTarget(Guid ItemId, string Path, IReadOnlyList<ChapterEntry> Chapters, string? Source = null);
