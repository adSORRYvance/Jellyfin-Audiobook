using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// We save every Audnexus answer to disk, one file per region, kind and ASIN, so restarts don't send us back to the API.
/// The client decides how long an answer counts as fresh, this class only reads and writes files.
/// </summary>
public sealed partial class AudnexusCache
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly ILogger<AudnexusCache> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudnexusCache"/> class.
    /// </summary>
    /// <param name="folder">Where the cache lives, created on the first save.</param>
    /// <param name="logger">Logger.</param>
    public AudnexusCache(string folder, ILogger<AudnexusCache> logger)
    {
        _folder = folder;
        _logger = logger;
    }

    /// <summary>
    /// Reads a saved answer.
    /// </summary>
    /// <param name="region">The Audible store, already checked by the caller.</param>
    /// <param name="kind">books, chapters or authors.</param>
    /// <param name="asin">The ASIN, already checked by the caller so it's safe in a file name.</param>
    /// <returns>The saved answer, or null when there's none or it can't be read.</returns>
    public CachedResponse? Read(string region, string kind, string asin)
    {
        var path = PathFor(region, kind, asin);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CachedResponse>(File.ReadAllText(path), _jsonOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A cache file is only a copy, so a bad one just means asking Audnexus again
            LogUnreadable(ex, path);
            return null;
        }
    }

    /// <summary>
    /// Saves an answer, replacing any older one.
    /// </summary>
    /// <param name="region">The Audible store, already checked by the caller.</param>
    /// <param name="kind">books, chapters or authors.</param>
    /// <param name="asin">The ASIN, already checked by the caller so it's safe in a file name.</param>
    /// <param name="response">The answer.</param>
    public void Write(string region, string kind, string asin, CachedResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var path = PathFor(region, kind, asin);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Two requests for the same book can save at once, so each gets its own temp file before the swap
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(response, _jsonOptions));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // We still have the answer in hand, failing to save it only costs a request next time
            LogUnwritable(ex, path);
        }
    }

    private string PathFor(string region, string kind, string asin) => Path.Combine(_folder, region, kind, asin + ".json");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the Audnexus cache file {Path}, asking Audnexus again")]
    private partial void LogUnreadable(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the Audnexus cache file {Path}")]
    private partial void LogUnwritable(Exception ex, string path);
}
