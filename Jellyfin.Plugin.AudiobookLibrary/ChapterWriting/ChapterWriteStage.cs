namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// Which step of writing chapters is running.
/// </summary>
public enum ChapterWriteStage
{
    /// <summary>
    /// Checking the folder and reading the original.
    /// </summary>
    Preparing,

    /// <summary>
    /// ffmpeg is writing the copy with new chapters.
    /// </summary>
    Writing,

    /// <summary>
    /// Comparing the copy with the original.
    /// </summary>
    Checking,

    /// <summary>
    /// Putting the copy in place of the original.
    /// </summary>
    Replacing,

    /// <summary>
    /// Asking Jellyfin to read the file again.
    /// </summary>
    Refreshing
}
