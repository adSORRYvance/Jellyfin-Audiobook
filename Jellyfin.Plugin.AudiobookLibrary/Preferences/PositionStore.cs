using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Preferences;

/// <summary>
/// We keep each user's place in their multi-file books, since Jellyfin only knows a position inside one file.
/// It's a separate file from speeds because the player saves it every few seconds, and a damaged one should never reset a speed.
/// </summary>
public sealed partial class PositionStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly TimeProvider _time;
    private readonly ILogger<PositionStore> _logger;

    // One lock for every user is still plenty, each player saves at most every 15 s
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PositionStore"/> class.
    /// </summary>
    /// <param name="folder">Where the per-user files go, created on the first save.</param>
    /// <param name="time">The clock, so tests get fixed times.</param>
    /// <param name="logger">Logger.</param>
    public PositionStore(string folder, TimeProvider time, ILogger<PositionStore> logger)
    {
        _folder = folder;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Rounds a position to tenths of a second and checks it's one a book can have.
    /// </summary>
    /// <param name="positionSec">The position a client sent.</param>
    /// <param name="normalized">The rounded position.</param>
    /// <returns>False when the position is negative or not a number.</returns>
    public static bool TryNormalize(double positionSec, out double normalized)
    {
        normalized = Math.Round(positionSec, 1, MidpointRounding.AwayFromZero);
        return double.IsFinite(normalized) && normalized >= 0;
    }

    /// <summary>
    /// Gets a user's place in a book.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="bookId">The book's key.</param>
    /// <returns>The saved place, or null when there's none.</returns>
    public SavedPosition? Get(Guid userId, Guid bookId)
    {
        lock (_lock)
        {
            return Read(userId).GetValueOrDefault(Key(bookId));
        }
    }

    /// <summary>
    /// Saves a user's place in a book.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="bookId">The book's key.</param>
    /// <param name="positionSec">A position that already passed <see cref="TryNormalize"/>.</param>
    /// <param name="title">The book's title, kept only to make the file readable.</param>
    /// <returns>What was saved.</returns>
    public SavedPosition Set(Guid userId, Guid bookId, double positionSec, string title)
    {
        lock (_lock)
        {
            var positions = Read(userId);
            var saved = new SavedPosition(positionSec, _time.GetUtcNow(), title);
            positions[Key(bookId)] = saved;

            Directory.CreateDirectory(_folder);
            var path = PathFor(userId);

            // Writing next to the file and then swapping it in means a crash mid-save leaves the old file, not half a file
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(positions, _jsonOptions));
            File.Move(temp, path, true);
            return saved;
        }
    }

    private static string Key(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    private string PathFor(Guid userId) => Path.Combine(_folder, Key(userId) + ".json");

    private Dictionary<string, SavedPosition> Read(Guid userId)
    {
        var path = PathFor(userId);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SavedPosition>>(File.ReadAllText(path), _jsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // Losing someone's places beats a player that can't open, and the old file is kept aside for a look
            LogDamagedFile(ex, path);
            File.Move(path, path + ".bad", true);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}, starting that user's book positions over and keeping the old file as .bad")]
    private partial void LogDamagedFile(Exception ex, string path);
}
