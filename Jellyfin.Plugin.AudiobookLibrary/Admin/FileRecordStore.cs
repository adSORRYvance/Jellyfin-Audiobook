using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// We keep every file's ASIN and chapter source in one JSON file, under Jellyfin's data folder so plugin updates keep it.
/// A record whose id Jellyfin no longer uses is found again by path, then by folder and file name, and moved to the new id.
/// </summary>
public sealed partial class FileRecordStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<FileRecordStore> _logger;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileRecordStore"/> class.
    /// </summary>
    /// <param name="path">The JSON file, created on the first save.</param>
    /// <param name="logger">Logger.</param>
    public FileRecordStore(string path, ILogger<FileRecordStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    /// <summary>
    /// Gets the records for many files at once, for the book list.
    /// </summary>
    /// <param name="files">Each file's current id and path.</param>
    /// <returns>The records found, by current id.</returns>
    public IReadOnlyDictionary<Guid, FileRecord> GetMany(IEnumerable<(Guid ItemId, string Path)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var current = files.ToList();
        var currentIds = current.Select(f => Key(f.ItemId)).ToHashSet();

        lock (_lock)
        {
            var records = Read();
            var changed = false;
            var found = new Dictionary<Guid, FileRecord>();
            foreach (var (itemId, path) in current)
            {
                // Only records whose file Jellyfin no longer has can move, so one file never takes another's ASIN
                var record = Find(records, itemId, path, r => !currentIds.Contains(Key(r.ItemId)), ref changed);
                if (record is not null)
                {
                    found[itemId] = record;
                }
            }

            if (changed)
            {
                Save(records);
            }

            return found;
        }
    }

    /// <summary>
    /// Gets one file's record.
    /// </summary>
    /// <param name="itemId">The file's current item id.</param>
    /// <param name="path">The file's current path.</param>
    /// <returns>The record, or null when the file has none.</returns>
    public FileRecord? Get(Guid itemId, string path)
    {
        lock (_lock)
        {
            var records = Read();
            var changed = false;
            var record = Find(records, itemId, path, null, ref changed);
            if (changed)
            {
                Save(records);
            }

            return record;
        }
    }

    /// <summary>
    /// Saves or clears a file's ASIN.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <param name="path">The file.</param>
    /// <param name="asin">A checked ASIN, or null to clear it.</param>
    /// <param name="region">The checked region it belongs to.</param>
    public void SetAsin(Guid itemId, string path, string? asin, string? region)
        => Update(itemId, path, r => r with { Asin = asin, Region = asin is null ? null : region });

    /// <summary>
    /// Records that we wrote a file's chapters.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <param name="path">The file.</param>
    /// <param name="source">Audible or Silence.</param>
    /// <param name="count">How many chapters.</param>
    /// <param name="at">When.</param>
    public void RecordApply(Guid itemId, string path, string source, int count, DateTimeOffset at)
        => Update(itemId, path, r => r with { ChapterSource = source, AppliedCount = count, AppliedAt = at });

    /// <summary>
    /// Forgets where a file's chapters came from, after its original was restored.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <param name="path">The file.</param>
    public void ClearApply(Guid itemId, string path)
        => Update(itemId, path, r => r with { ChapterSource = null, AppliedCount = null, AppliedAt = null });

    private static string Key(Guid itemId) => itemId.ToString("N", CultureInfo.InvariantCulture);

    // The last folder and the file name, which survive moving the whole library to a new root
    private static string Tail(string path)
        => System.IO.Path.Combine(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? string.Empty, System.IO.Path.GetFileName(path));

    // A path is one file, so a record at the same path always moves to the file's new id
    // The folder and file name could be another book's, so that fallback needs to know which records lost their file
    private static FileRecord? Find(Dictionary<string, FileRecord> records, Guid itemId, string path, Func<FileRecord, bool>? orphaned, ref bool changed)
    {
        if (records.TryGetValue(Key(itemId), out var byId))
        {
            return byId;
        }

        // Only a single match counts, two files with the same folder and name would be a guess
        var match = records.Values.Where(r => r.Path == path).ToList();
        if (match.Count == 0 && orphaned is not null)
        {
            match = records.Values.Where(r => orphaned(r) && Tail(r.Path) == Tail(path)).ToList();
        }

        if (match.Count != 1)
        {
            return null;
        }

        var moved = match[0] with { ItemId = itemId, Path = path };
        records.Remove(Key(match[0].ItemId));
        records[Key(itemId)] = moved;
        changed = true;
        return moved;
    }

    private void Update(Guid itemId, string path, Func<FileRecord, FileRecord> change)
    {
        lock (_lock)
        {
            var records = Read();
            var changed = false;
            var current = Find(records, itemId, path, null, ref changed) ?? new FileRecord(itemId, path, null, null, null, null, null);
            var updated = change(current with { Path = path });

            if (updated.IsEmpty)
            {
                records.Remove(Key(itemId));
            }
            else
            {
                records[Key(itemId)] = updated;
            }

            Save(records);
        }
    }

    private Dictionary<string, FileRecord> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FileRecord>>(File.ReadAllText(_path), _jsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // Typed-in ASINs are worth keeping, so the damaged file is set aside rather than overwritten
            LogDamagedFile(ex, _path);
            File.Move(_path, _path + ".bad", true);
            return [];
        }
    }

    private void Save(Dictionary<string, FileRecord> records)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, _jsonOptions));
        File.Move(temp, _path, true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}, starting over and keeping the old file as .bad")]
    private partial void LogDamagedFile(Exception ex, string path);
}
