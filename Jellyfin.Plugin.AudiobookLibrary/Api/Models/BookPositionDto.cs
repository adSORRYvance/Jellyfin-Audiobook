using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// A user's place in a whole book.
/// </summary>
/// <param name="PositionSec">Seconds from the start of the book's first file.</param>
/// <param name="UpdatedAt">When it was last saved, null when it never was. Ignored on a save.</param>
public sealed record BookPositionDto(double PositionSec, DateTimeOffset? UpdatedAt = null);
