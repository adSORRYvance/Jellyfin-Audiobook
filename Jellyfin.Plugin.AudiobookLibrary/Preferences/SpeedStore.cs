using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Preferences;

/// <summary>
/// We keep each user's book speeds in their own small JSON file, so a damaged file only ever resets one person.
/// The folder lives outside the plugins folder, because Jellyfin deletes old plugin version folders on update.
/// </summary>
public sealed partial class SpeedStore
{
    /// <summary>
    /// The speed a book plays at until the user picks another.
    /// </summary>
    public const double DefaultSpeed = 1.0;

    /// <summary>
    /// The slowest speed we accept, the same as the player's.
    /// </summary>
    public const double MinSpeed = 0.5;

    /// <summary>
    /// The fastest speed we accept, the same as the player's.
    /// </summary>
    public const double MaxSpeed = 3.0;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly ILogger<SpeedStore> _logger;

    // One lock for every user is plenty, a save is a few hundred bytes and only happens when someone changes speed
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SpeedStore"/> class.
    /// </summary>
    /// <param name="folder">Where the per-user files go, created on the first save.</param>
    /// <param name="logger">Logger.</param>
    public SpeedStore(string folder, ILogger<SpeedStore> logger)
    {
        _folder = folder;
        _logger = logger;
    }

    /// <summary>
    /// Rounds a speed to the player's 0.1 steps and checks it's one the player can play.
    /// </summary>
    /// <param name="speed">The speed a client sent.</param>
    /// <param name="normalized">The rounded speed.</param>
    /// <returns>False when the speed is outside the player's range or not a number.</returns>
    public static bool TryNormalize(double speed, out double normalized)
    {
        normalized = Math.Round(speed, 1, MidpointRounding.AwayFromZero);
        return double.IsFinite(normalized) && normalized >= MinSpeed && normalized <= MaxSpeed;
    }

    /// <summary>
    /// Gets a user's speed for a book.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="bookId">The book's key.</param>
    /// <returns>The saved speed, or the default when there's none.</returns>
    public double Get(Guid userId, Guid bookId)
    {
        lock (_lock)
        {
            return Read(userId).TryGetValue(Key(bookId), out var saved) ? saved.Speed : DefaultSpeed;
        }
    }

    /// <summary>
    /// Saves a user's speed for a book.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="bookId">The book's key.</param>
    /// <param name="speed">A speed that already passed <see cref="TryNormalize"/>.</param>
    /// <param name="title">The book's title, kept only to make the file readable.</param>
    public void Set(Guid userId, Guid bookId, double speed, string title)
    {
        lock (_lock)
        {
            var speeds = Read(userId);
            speeds[Key(bookId)] = new SavedSpeed(speed, title);

            Directory.CreateDirectory(_folder);
            var path = PathFor(userId);

            // Writing next to the file and then swapping it in means a crash mid-save leaves the old file, not half a file
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(speeds, _jsonOptions));
            File.Move(temp, path, true);
        }
    }

    private static string Key(Guid bookId) => bookId.ToString("N", CultureInfo.InvariantCulture);

    private string PathFor(Guid userId) => Path.Combine(_folder, Key(userId) + ".json");

    private Dictionary<string, SavedSpeed> Read(Guid userId)
    {
        var path = PathFor(userId);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SavedSpeed>>(File.ReadAllText(path), _jsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // Losing someone's speeds beats a player that can't open, and the old file is kept aside for a look
            LogDamagedFile(ex, path);
            File.Move(path, path + ".bad", true);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}, starting that user's speeds over and keeping the old file as .bad")]
    private partial void LogDamagedFile(Exception ex, string path);
}
