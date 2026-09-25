using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// One audio file of a book, copied out of Jellyfin's item so the timeline code never touches server types.
/// </summary>
/// <param name="ItemId">The file's AudioBook item id.</param>
/// <param name="FileName">The file name without folders.</param>
/// <param name="IndexNumber">The track tag, if the file has one.</param>
/// <param name="RunTimeTicks">The file's length.</param>
/// <param name="Chapters">The chapters Jellyfin found inside the file.</param>
public sealed record TrackSource(
    Guid ItemId,
    string FileName,
    int? IndexNumber,
    long RunTimeTicks,
    IReadOnlyList<ChapterMark> Chapters);
