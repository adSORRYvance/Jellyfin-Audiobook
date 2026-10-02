using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// We save each finished scan to disk, one file per item and loudness, so a restart doesn't cost another pass through the book.
/// </summary>
public sealed partial class SilenceCache
{
    private readonly string _folder;
    private readonly ILogger<SilenceCache> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SilenceCache"/> class.
    /// </summary>
    /// <param name="folder">Where scans are saved, created on the first save.</param>
    /// <param name="logger">Logger.</param>
    public SilenceCache(string folder, ILogger<SilenceCache> logger)
    {
        _folder = folder;
        _logger = logger;
    }

    /// <summary>
    /// Reads a saved scan.
    /// </summary>
    /// <param name="itemId">The scanned item.</param>
    /// <param name="noiseDb">The loudness it was scanned at.</param>
    /// <returns>The scan, or null when there's none or it can't be read.</returns>
    public SilenceScan? Read(Guid itemId, int noiseDb)
    {
        var path = PathFor(itemId, noiseDb);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<SilenceScan>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A saved scan is only a shortcut, a bad one just means scanning again
            LogUnreadable(ex, path);
            return null;
        }
    }

    /// <summary>
    /// Saves a scan, replacing any older one for the same item and loudness.
    /// </summary>
    /// <param name="scan">The scan.</param>
    public void Write(SilenceScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var path = PathFor(scan.ItemId, scan.NoiseDb);
        try
        {
            Directory.CreateDirectory(_folder);
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(scan));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The scan still reaches whoever asked for it, it just won't survive a restart
            LogUnwritable(ex, path);
        }
    }

    /// <summary>
    /// Moves an item's saved scans from the old file to its replacement, when the sound is known to be the same.
    /// Only scans that matched the old file move, one that was already out of date stays that way.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="path">The file.</param>
    /// <param name="oldSize">The old file's size.</param>
    /// <param name="oldModifiedUtc">The old file's modified time.</param>
    /// <param name="newSize">The new file's size.</param>
    /// <param name="newModifiedUtc">The new file's modified time.</param>
    /// <returns>How many scans moved.</returns>
    public int Restamp(Guid itemId, string path, long oldSize, DateTime oldModifiedUtc, long newSize, DateTime newModifiedUtc)
    {
        if (!Directory.Exists(_folder))
        {
            return 0;
        }

        var moved = 0;
        var pattern = string.Create(CultureInfo.InvariantCulture, $"{itemId:N}.*.json");
        foreach (var file in Directory.EnumerateFiles(_folder, pattern))
        {
            var noise = Path.GetFileNameWithoutExtension(file).Split('.')[^1];
            if (!int.TryParse(noise, NumberStyles.Integer, CultureInfo.InvariantCulture, out var noiseDb))
            {
                continue;
            }

            var scan = Read(itemId, noiseDb);
            if (scan is not null && scan.Path == path && scan.FileSize == oldSize && scan.FileModifiedUtc == oldModifiedUtc)
            {
                Write(scan with { FileSize = newSize, FileModifiedUtc = newModifiedUtc });
                moved++;
            }
        }

        return moved;
    }

    private string PathFor(Guid itemId, int noiseDb)
        => Path.Combine(_folder, string.Create(CultureInfo.InvariantCulture, $"{itemId:N}.{noiseDb}.json"));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the saved silence scan {Path}, it will be scanned again")]
    private partial void LogUnreadable(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the silence scan {Path}")]
    private partial void LogUnwritable(Exception ex, string path);
}
