namespace Jellyfin.Plugin.AudiobookLibrary.Preferences;

/// <summary>
/// One book's speed in a user's speeds file.
/// </summary>
/// <param name="Speed">The speed the user last picked for this book.</param>
/// <param name="Title">The book's title when it was saved, only there so the file makes sense to a person reading it.</param>
public sealed record SavedSpeed(double Speed, string Title);
