using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// One audiobook file as the admin page lists it.
/// </summary>
/// <param name="ItemId">The file's AudioBook item.</param>
/// <param name="FileName">The file's name without its folder.</param>
/// <param name="Type">The extension without the dot, like m4b or mp3.</param>
/// <param name="DurationSec">The file's length.</param>
/// <param name="ChapterCount">How many chapters Jellyfin has for it.</param>
/// <param name="Status">NoChapters, Embedded, Audible, Silence, NeedsReview or NotSupported.</param>
/// <param name="Asin">The ASIN an admin matched it to.</param>
/// <param name="Region">The Audible store that ASIN is from.</param>
/// <param name="HasBackup">Whether the original is kept as a .bak, so it can be restored.</param>
/// <param name="CanWrite">Whether we can write chapters into this file type.</param>
public sealed record AdminFileDto(
    Guid ItemId,
    string FileName,
    string Type,
    double DurationSec,
    int ChapterCount,
    string Status,
    string? Asin,
    string? Region,
    bool HasBackup,
    bool CanWrite);
