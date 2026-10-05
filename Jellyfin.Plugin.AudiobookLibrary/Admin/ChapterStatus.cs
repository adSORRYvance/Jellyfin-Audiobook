using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// We work out a file's status pill from what Jellyfin has and what we remember writing.
/// </summary>
public static class ChapterStatus
{
    /// <summary>
    /// Picks a file's status.
    /// </summary>
    /// <param name="canWrite">Whether we can write chapters into this file type.</param>
    /// <param name="chapterStartsSec">Where Jellyfin's chapters for the file start.</param>
    /// <param name="record">What we saved about the file, if anything.</param>
    /// <param name="refreshedSinceApply">Whether Jellyfin re-read the file after we wrote it.</param>
    /// <returns>The status.</returns>
    public static FileChapterStatus Classify(bool canWrite, IReadOnlyList<double> chapterStartsSec, FileRecord? record, bool refreshedSinceApply)
    {
        ArgumentNullException.ThrowIfNull(chapterStartsSec);

        if (!canWrite)
        {
            return FileChapterStatus.NotSupported;
        }

        if (record?.ChapterSource is { } source)
        {
            // Until Jellyfin re-reads the file it still has the old chapters, so a different count means nothing yet
            if (refreshedSinceApply && chapterStartsSec.Count != record.AppliedCount)
            {
                return FileChapterStatus.NeedsReview;
            }

            return source == nameof(FileChapterStatus.Audible) ? FileChapterStatus.Audible : FileChapterStatus.Silence;
        }

        // The same rule as the player: no chapters, or a lone one at 0, is a book without chapters
        var hasReal = chapterStartsSec.Count > 1 || (chapterStartsSec.Count == 1 && chapterStartsSec[0] > 0);
        return hasReal ? FileChapterStatus.Embedded : FileChapterStatus.NoChapters;
    }
}
