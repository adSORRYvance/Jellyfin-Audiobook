using Jellyfin.Plugin.AudiobookLibrary.Admin;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Jellyfin.Plugin.AudiobookLibrary.Silences;
using Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Admin;

public sealed class AdminChapterListenerTests : IDisposable
{
    private const string Book = "/books/Acorna/Acorna.m4b";

    private static readonly FileStamp Original = new(240637958, new DateTime(2026, 9, 24, 22, 1, 0, DateTimeKind.Utc));
    private static readonly FileStamp Rewritten = new(240639543, new DateTime(2026, 10, 2, 17, 57, 0, DateTimeKind.Utc));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-listener-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _id = Guid.NewGuid();
    private readonly FakeClock _clock = new();
    private readonly FileRecordStore _records;
    private readonly SilenceCache _silences;
    private readonly AdminChapterListener _listener;

    public AdminChapterListenerTests()
    {
        _records = new FileRecordStore(Path.Combine(_folder, "files.json"), NullLogger<FileRecordStore>.Instance);
        _silences = new SilenceCache(Path.Combine(_folder, "silences"), NullLogger<SilenceCache>.Instance);
        _listener = new AdminChapterListener(_records, _silences, _clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private SilenceScan Scan(FileStamp stamp, int noiseDb = -30)
        => new(_id, Book, stamp.Size, stamp.ModifiedUtc, noiseDb, 35023.6, DateTimeOffset.UnixEpoch, [new Silence(100, 104, 4)]);

    private ChapterWriteTarget Target(string? source) => new(_id, Book, [new("One", 0), new("Two", 104)], source);

    [Fact]
    public void Write_RecordsTheSourceAndCount()
    {
        _listener.FileReplaced(Target("Silence"), Original, Rewritten);

        var record = _records.Get(_id, Book);
        Assert.Equal("Silence", record?.ChapterSource);
        Assert.Equal(2, record?.AppliedCount);
        Assert.Equal(_clock.Now, record?.AppliedAt);
    }

    [Fact]
    public void Write_MovesEverySilenceScanOfTheOldFileToTheNewOne()
    {
        _silences.Write(Scan(Original, -30));
        _silences.Write(Scan(Original, -40));

        _listener.FileReplaced(Target("Silence"), Original, Rewritten);

        Assert.Equal(Rewritten.Size, _silences.Read(_id, -30)?.FileSize);
        Assert.Equal(Rewritten.ModifiedUtc, _silences.Read(_id, -40)?.FileModifiedUtc);
    }

    [Fact]
    public void Write_LeavesAScanThatWasAlreadyOutOfDate()
    {
        var older = new FileStamp(1, DateTime.UnixEpoch);
        _silences.Write(Scan(older));

        _listener.FileReplaced(Target("Silence"), Original, Rewritten);

        Assert.Equal(older.Size, _silences.Read(_id, -30)?.FileSize);
    }

    [Fact]
    public void WriteWithoutASource_RecordsNothing()
    {
        _listener.FileReplaced(Target(null), Original, Rewritten);

        Assert.Null(_records.Get(_id, Book));
    }

    [Fact]
    public void Restore_ForgetsTheSourceButKeepsTheAsin_AndMovesTheScanBack()
    {
        _records.SetAsin(_id, Book, "B0ACORNA01", "us");
        _silences.Write(Scan(Original));
        _listener.FileReplaced(Target("Audible"), Original, Rewritten);

        _listener.FileRestored(_id, Book, Rewritten, Original);

        var record = _records.Get(_id, Book);
        Assert.Equal("B0ACORNA01", record?.Asin);
        Assert.Null(record?.ChapterSource);
        Assert.Equal(Original.Size, _silences.Read(_id, -30)?.FileSize);
    }
}
