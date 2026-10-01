namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// Where a silence scan is.
/// </summary>
public enum SilenceJobState
{
    /// <summary>
    /// Waiting for another scan to finish, only one runs at a time.
    /// </summary>
    Queued,

    /// <summary>
    /// ffmpeg is reading the file.
    /// </summary>
    Running,

    /// <summary>
    /// Finished, the silences are saved.
    /// </summary>
    Done,

    /// <summary>
    /// ffmpeg failed, the error says why.
    /// </summary>
    Failed,

    /// <summary>
    /// Stopped before it finished.
    /// </summary>
    Cancelled
}
