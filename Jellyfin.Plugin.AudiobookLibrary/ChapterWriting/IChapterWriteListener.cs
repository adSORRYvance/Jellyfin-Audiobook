using System;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// Hears about files the chapter writer replaced, so saved data tied to the old file can follow it.
/// Only called once the new file is in place, and a listener that throws can't undo that.
/// </summary>
public interface IChapterWriteListener
{
    /// <summary>
    /// Called after new chapters replaced a file.
    /// </summary>
    /// <param name="target">What was written, including where the chapters came from.</param>
    /// <param name="before">The file's stamp before the write.</param>
    /// <param name="after">The file's stamp now.</param>
    void FileReplaced(ChapterWriteTarget target, FileStamp before, FileStamp after);

    /// <summary>
    /// Called after a file was put back from its backup.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <param name="path">The file.</param>
    /// <param name="before">The file's stamp before the restore.</param>
    /// <param name="after">The file's stamp now.</param>
    void FileRestored(Guid itemId, string path, FileStamp before, FileStamp after);
}
