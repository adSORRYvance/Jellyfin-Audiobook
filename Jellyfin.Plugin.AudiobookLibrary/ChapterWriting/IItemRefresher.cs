using System;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// Asks Jellyfin to read an item's file again. Behind an interface so the job tests don't need a library.
/// </summary>
public interface IItemRefresher
{
    /// <summary>
    /// Queues a refresh that re-reads the file, including its chapters.
    /// </summary>
    /// <param name="itemId">The item whose file changed.</param>
    void Refresh(Guid itemId);
}
