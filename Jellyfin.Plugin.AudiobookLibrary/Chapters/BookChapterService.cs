using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// We gather a book's files, playlist and saved chapters from Jellyfin and hand them to the timeline code.
/// A book is every AudioBook in the folder of the file we were asked about, since Jellyfin has no item for the whole book.
/// </summary>
public partial class BookChapterService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IChapterManager _chapterManager;
    private readonly ILogger<BookChapterService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BookChapterService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager, used to find the book's files.</param>
    /// <param name="chapterManager">Chapter manager, used for the chapters Jellyfin saved during the scan.</param>
    /// <param name="logger">Logger.</param>
    public BookChapterService(ILibraryManager libraryManager, IChapterManager chapterManager, ILogger<BookChapterService> logger)
    {
        _libraryManager = libraryManager;
        _chapterManager = chapterManager;
        _logger = logger;
    }

    /// <summary>
    /// Builds the whole book around one of its files.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <param name="user">The calling user, or null for an API key.</param>
    /// <returns>The book layout, or null when the item is missing, hidden from the user or not an audiobook.</returns>
    public BookLayout? GetBook(Guid itemId, User? user)
    {
        if (_libraryManager.GetItemById<BaseItem>(itemId, user) is not AudioBook file)
        {
            return null;
        }

        var folder = file.GetParent() as Folder;
        var files = folder is null ? [file] : GetBookFiles(folder, user, file);

        var sources = files.Select(f => new TrackSource(
                f.Id,
                Path.GetFileName(f.Path ?? string.Empty),
                f.IndexNumber,
                f.RunTimeTicks ?? 0,
                _chapterManager.GetChapters(f.Id).Select(c => new ChapterMark(c.Name, c.StartPositionTicks)).ToList()))
            .ToList();

        var playlist = folder is null ? null : ReadPlaylist(folder.Path);
        return BookTimeline.Build(TrackOrder.Sort(sources, playlist));
    }

    private List<AudioBook> GetBookFiles(Folder folder, User? user, AudioBook requested)
    {
        var files = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ParentId = folder.Id,
                IncludeItemTypes = [BaseItemKind.AudioBook],
                Recursive = false
            })
            .OfType<AudioBook>()
            .ToList();

        // The query honors the user's access, but the file we were asked about must always be part of its own book
        if (!files.Any(f => f.Id == requested.Id))
        {
            files.Add(requested);
        }

        return files;
    }

    private List<PlaylistEntry>? ReadPlaylist(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return null;
        }

        try
        {
            // Sorted so a folder with two playlists always picks the same one
            var path = Directory.EnumerateFiles(folderPath, "*.m3u8")
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return path is null ? null : M3u8Parser.Parse(File.ReadAllText(path)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the playlist we still have tags and file names, so this is worth a warning but not a failure
            LogPlaylistFailed(ex, folderPath);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the playlist in {Folder}, ordering by tags and file names instead")]
    private partial void LogPlaylistFailed(Exception ex, string folder);
}
