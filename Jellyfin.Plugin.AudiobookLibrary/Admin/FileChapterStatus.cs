namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// The status pill the admin page shows next to each file.
/// </summary>
public enum FileChapterStatus
{
    /// <summary>
    /// No chapters, or only a placeholder at 0.
    /// </summary>
    NoChapters,

    /// <summary>
    /// Real chapters we didn't write.
    /// </summary>
    Embedded,

    /// <summary>
    /// We wrote chapters from Audible.
    /// </summary>
    Audible,

    /// <summary>
    /// We wrote chapters from detected silences.
    /// </summary>
    Silence,

    /// <summary>
    /// We wrote chapters, but the file has a different number now, so it was probably replaced.
    /// </summary>
    NeedsReview,

    /// <summary>
    /// A file type we can't write chapters into yet, like MP3.
    /// </summary>
    NotSupported
}
