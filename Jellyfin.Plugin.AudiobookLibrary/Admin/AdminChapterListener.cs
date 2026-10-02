using System;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Jellyfin.Plugin.AudiobookLibrary.Silences;

namespace Jellyfin.Plugin.AudiobookLibrary.Admin;

/// <summary>
/// After a write or restore we note where the file's chapters came from, and keep its silence scans.
/// The writer has just proven the sound is the same, so the silences are too, and the preview needn't be scanned again.
/// </summary>
public sealed class AdminChapterListener : IChapterWriteListener
{
    private readonly FileRecordStore _records;
    private readonly SilenceCache _silences;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminChapterListener"/> class.
    /// </summary>
    /// <param name="records">Where each file's chapter source is kept.</param>
    /// <param name="silences">The saved silence scans.</param>
    /// <param name="time">The clock.</param>
    public AdminChapterListener(FileRecordStore records, SilenceCache silences, TimeProvider time)
    {
        _records = records;
        _silences = silences;
        _time = time;
    }

    /// <inheritdoc />
    public void FileReplaced(ChapterWriteTarget target, FileStamp before, FileStamp after)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (target.Source is not null)
        {
            _records.RecordApply(target.ItemId, target.Path, target.Source, target.Chapters.Count, _time.GetUtcNow());
        }

        _silences.Restamp(target.ItemId, target.Path, before.Size, before.ModifiedUtc, after.Size, after.ModifiedUtc);
    }

    /// <inheritdoc />
    public void FileRestored(Guid itemId, string path, FileStamp before, FileStamp after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        _records.ClearApply(itemId, path);
        _silences.Restamp(itemId, path, before.Size, before.ModifiedUtc, after.Size, after.ModifiedUtc);
    }
}
