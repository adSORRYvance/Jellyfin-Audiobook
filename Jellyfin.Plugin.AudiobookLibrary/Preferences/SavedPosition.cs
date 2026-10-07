using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Preferences;

/// <summary>
/// One book's place in a user's positions file.
/// </summary>
/// <param name="PositionSec">Where the user is in the whole book, in seconds from the start of its first file.</param>
/// <param name="UpdatedAt">When the player last saved it.</param>
/// <param name="Title">The book's title when it was saved, only there so the file makes sense to a person reading it.</param>
public sealed record SavedPosition(double PositionSec, DateTimeOffset UpdatedAt, string Title);
