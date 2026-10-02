using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;
using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// We list every audiobook file for the admin page, grouped into books the same way the player does, one folder per book.
/// </summary>
public sealed class AdminLibrary
{
    private readonly ILibraryManager _libraryManager;
    private readonly IChapterManager _chapterManager;
    private readonly FileRecordStore _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminLibrary"/> class.
    /// </summary>
    /// <param name="libraryManager">Finds the audiobook files.</param>
    /// <param name="chapterManager">The chapters Jellyfin saved for each file.</param>
    /// <param name="records">Each file's ASIN and chapter source.</param>
    public AdminLibrary(ILibraryManager libraryManager, IChapterManager chapterManager, FileRecordStore records)
    {
        _libraryManager = libraryManager;
        _chapterManager = chapterManager;
        _records = records;
    }

    /// <summary>
    /// Finds an audiobook file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>The file, or null when the id isn't an audiobook file.</returns>
    public AudioBook? FindFile(Guid itemId)
        => _libraryManager.GetItemById(itemId) is AudioBook file && !string.IsNullOrEmpty(file.Path) ? file : null;

    /// <summary>
    /// Lists every audiobook file, grouped into books.
    /// </summary>
    /// <returns>The books, by author and then title.</returns>
    public IReadOnlyList<AdminBookDto> GetBooks()
    {
        // Admin only, so every library and every file, without a user's access rules
        var files = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.AudioBook],
                Recursive = true
            })
            .OfType<AudioBook>()
            .Where(f => !string.IsNullOrEmpty(f.Path))
            .ToList();

        var records = _records.GetMany(files.Select(f => (f.Id, f.Path)));

        return files
            .GroupBy(f => f.ParentId == Guid.Empty ? f.Id : f.ParentId)
            .Select(group => ToBook(group.Key, group.ToList(), records))
            .OrderBy(b => b.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds one file's row.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="record">What we saved about it.</param>
    /// <returns>The row.</returns>
    public AdminFileDto ToFile(AudioBook file, FileRecord? record)
    {
        ArgumentNullException.ThrowIfNull(file);

        var starts = _chapterManager.GetChapters(file.Id).Select(c => c.StartPositionTicks / (double)TimeSpan.TicksPerSecond).ToList();
        var canWrite = M4bChapterWriter.CanWrite(file.Path);
        var refreshed = record?.AppliedAt is { } applied && new DateTimeOffset(DateTime.SpecifyKind(file.DateLastRefreshed, DateTimeKind.Utc)) > applied;

        return new AdminFileDto(
            file.Id,
            Path.GetFileName(file.Path),
            Path.GetExtension(file.Path).TrimStart('.').ToLowerInvariant(),
            (file.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond,
            starts.Count,
            ChapterStatus.Classify(canWrite, starts, record, refreshed).ToString(),
            record?.Asin,
            record?.Region,
            File.Exists(M4bChapterWriter.BackupPath(file.Path)),
            canWrite);
    }

    private AdminBookDto ToBook(Guid folderId, List<AudioBook> files, IReadOnlyDictionary<Guid, FileRecord> records)
    {
        var first = files[0];
        var title = first.Album ?? first.GetParent()?.Name ?? first.Name ?? string.Empty;
        var author = first.AlbumArtists is { Count: > 0 } albumArtists ? albumArtists[0]
            : first.Artists is { Count: > 0 } artists ? artists[0]
            : null;

        var rows = files
            .OrderBy(f => Path.GetFileName(f.Path), NaturalStringComparer.Instance)
            .Select(f => ToFile(f, records.GetValueOrDefault(f.Id)))
            .ToList();
        return new AdminBookDto(folderId, title, author, rows);
    }
}
