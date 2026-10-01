using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// Finds the silences in a file. Behind an interface so the job tests don't need ffmpeg.
/// </summary>
public interface ISilenceScanner
{
    /// <summary>
    /// Reads the whole file and returns every silence of at least a second.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="noiseDb">Anything quieter than this counts as silence.</param>
    /// <param name="onProgress">Called with how many seconds of the file have been read.</param>
    /// <param name="cancellationToken">Stops the scan.</param>
    /// <returns>The silences.</returns>
    Task<IReadOnlyList<Silence>> ScanAsync(string path, int noiseDb, Action<double> onProgress, CancellationToken cancellationToken);
}
