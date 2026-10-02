using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// The three things the chapter writer needs from ffmpeg. Behind an interface so the safety logic can be tested without rewriting real files.
/// </summary>
public interface IMediaTool
{
    /// <summary>
    /// Reads a file's length, tags and chapters.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Stops ffprobe.</param>
    /// <returns>What ffprobe found.</returns>
    Task<MediaFacts> ProbeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Hashes every audio packet, so two files with the same hash have exactly the same sound.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Stops ffmpeg.</param>
    /// <returns>The MD5 of the audio packets.</returns>
    Task<string> AudioHashAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a file with new chapters, without re-encoding anything.
    /// </summary>
    /// <param name="sourcePath">The original file.</param>
    /// <param name="chapterFilePath">The FFMETADATA chapter list.</param>
    /// <param name="destinationPath">Where the copy goes.</param>
    /// <param name="cancellationToken">Stops ffmpeg.</param>
    /// <returns>A task that finishes once the copy is written.</returns>
    Task WriteChaptersAsync(string sourcePath, string chapterFilePath, string destinationPath, CancellationToken cancellationToken);
}
