using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// Where one file sits in the book.
/// </summary>
/// <param name="ItemId">The file's AudioBook item id.</param>
/// <param name="StartTicks">Where the file starts in the book.</param>
/// <param name="DurationTicks">The file's length.</param>
public sealed record BookTrack(Guid ItemId, long StartTicks, long DurationTicks);
