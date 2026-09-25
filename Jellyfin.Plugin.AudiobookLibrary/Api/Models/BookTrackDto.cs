using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// Where one file sits in the book.
/// </summary>
/// <param name="ItemId">The file's AudioBook item id, which the player streams.</param>
/// <param name="StartSec">Where the file starts in the book.</param>
/// <param name="DurationSec">The file's length.</param>
public sealed record BookTrackDto(Guid ItemId, double StartSec, double DurationSec);
