using System;
using System.IO;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// A file's size and modified time, enough to tell whether it's still the file something was saved against.
/// </summary>
/// <param name="Size">The size in bytes.</param>
/// <param name="ModifiedUtc">The last write time.</param>
public sealed record FileStamp(long Size, DateTime ModifiedUtc)
{
    /// <summary>
    /// Reads a file's stamp.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The stamp.</returns>
    public static FileStamp Of(string path)
    {
        var file = new FileInfo(path);
        return new FileStamp(file.Length, file.LastWriteTimeUtc);
    }
}
