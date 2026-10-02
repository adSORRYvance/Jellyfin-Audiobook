namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// Where a chapter write is.
/// </summary>
public enum ChapterWriteState
{
    /// <summary>
    /// Waiting for another write to finish, only one runs at a time.
    /// </summary>
    Queued,

    /// <summary>
    /// Running, the stage says which step.
    /// </summary>
    Running,

    /// <summary>
    /// The new chapters are in the file.
    /// </summary>
    Done,

    /// <summary>
    /// Stopped with the original untouched, the error says why.
    /// </summary>
    Failed
}
