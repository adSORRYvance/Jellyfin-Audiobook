using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// What the admin page knows about one audiobook file: its ASIN and where its chapters came from.
/// The path is kept next to the id, because Jellyfin makes item ids from paths and a moved library gets new ids.
/// </summary>
/// <param name="ItemId">The file's AudioBook item when this was last saved.</param>
/// <param name="Path">The file's path when this was last saved.</param>
/// <param name="Asin">The Audible ASIN an admin matched it to.</param>
/// <param name="Region">The Audible store the ASIN belongs to.</param>
/// <param name="ChapterSource">Audible or Silence, when we wrote the file's chapters.</param>
/// <param name="AppliedAt">When we wrote them.</param>
/// <param name="AppliedCount">How many chapters we wrote, so a file replaced later shows up as needing review.</param>
public sealed record FileRecord(
    Guid ItemId,
    string Path,
    string? Asin,
    string? Region,
    string? ChapterSource,
    DateTimeOffset? AppliedAt,
    int? AppliedCount)
{
    /// <summary>
    /// Gets a value indicating whether there's nothing left worth keeping.
    /// </summary>
    public bool IsEmpty => Asin is null && ChapterSource is null;
}
