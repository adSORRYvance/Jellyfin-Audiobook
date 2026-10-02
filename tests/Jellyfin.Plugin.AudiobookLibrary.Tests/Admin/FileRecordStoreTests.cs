using Jellyfin.Plugin.AudiobookLibrary.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Admin;

public sealed class FileRecordStoreTests : IDisposable
{
    private const string Acorna = "/books/Anne McCaffrey/Acorna/Acorna.m4b";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-records-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _id = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    // A new store over the same file behaves like the server after a restart
    private FileRecordStore NewStore() => new(Path.Combine(_folder, "files.json"), NullLogger<FileRecordStore>.Instance);

    [Fact]
    public void SetAsin_IsReadBackAfterARestart()
    {
        NewStore().SetAsin(_id, Acorna, "B09N446YK7", "us");

        var record = NewStore().Get(_id, Acorna);

        Assert.Equal("B09N446YK7", record?.Asin);
        Assert.Equal("us", record?.Region);
    }

    [Fact]
    public void Get_Unknown_IsNull()
    {
        Assert.Null(NewStore().Get(_id, Acorna));
    }

    [Fact]
    public void RecordApply_KeepsTheAsin()
    {
        var store = NewStore();
        store.SetAsin(_id, Acorna, "B09N446YK7", "us");
        var at = new DateTimeOffset(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);

        store.RecordApply(_id, Acorna, "Audible", 94, at);

        Assert.Equal(new FileRecord(_id, Acorna, "B09N446YK7", "us", "Audible", at, 94), store.Get(_id, Acorna));
    }

    [Fact]
    public void ClearingEverything_RemovesTheRecord()
    {
        var store = NewStore();
        store.RecordApply(_id, Acorna, "Silence", 49, DateTimeOffset.UnixEpoch);

        store.ClearApply(_id, Acorna);

        Assert.Null(store.Get(_id, Acorna));
    }

    [Fact]
    public void NewItemId_SamePath_IsFoundAndMoved()
    {
        // Jellyfin gives a file a new id after things like a library rebuild
        NewStore().SetAsin(_id, Acorna, "B09N446YK7", "us");
        var newId = Guid.NewGuid();

        var record = NewStore().Get(newId, Acorna);

        Assert.Equal(newId, record?.ItemId);
        Assert.Equal("B09N446YK7", record?.Asin);
        Assert.Null(NewStore().GetMany([(_id, "/elsewhere/x.m4b")]).GetValueOrDefault(_id));
    }

    [Fact]
    public void LibraryMovedToANewRoot_IsFoundByFolderAndFileName()
    {
        // TM-216: moving /books/audiobooks to /books gave every item a new id and a new path
        NewStore().SetAsin(_id, "/books/audiobooks/Anne McCaffrey/Acorna/Acorna.m4b", "B0ACORNA01", "us");
        var newId = Guid.NewGuid();

        var record = NewStore().GetMany([(newId, Acorna)]).GetValueOrDefault(newId);

        Assert.Equal("B0ACORNA01", record?.Asin);
        Assert.Equal(Acorna, record?.Path);
        Assert.Equal("B0ACORNA01", NewStore().Get(newId, Acorna)?.Asin);
    }

    [Fact]
    public void SameFolderAndFileName_InAnotherBookThatStillExists_IsNotTaken()
    {
        // Two series can both have Book/Part 1.m4b, saving one mustn't move the other's ASIN
        var store = NewStore();
        var first = Guid.NewGuid();
        store.SetAsin(first, "/a/Book/Part 1.m4b", "B000000001", "us");

        store.SetAsin(_id, "/b/Book/Part 1.m4b", "B000000002", "us");

        var both = store.GetMany([(first, "/a/Book/Part 1.m4b"), (_id, "/b/Book/Part 1.m4b")]);
        Assert.Equal("B000000001", both[first].Asin);
        Assert.Equal("B000000002", both[_id].Asin);
    }

    [Fact]
    public void TwoLostRecordsWithTheSameName_IsNotAGuess()
    {
        var store = NewStore();
        store.SetAsin(Guid.NewGuid(), "/a/Book/Part 1.m4b", "B000000001", "us");
        store.SetAsin(Guid.NewGuid(), "/b/Book/Part 1.m4b", "B000000002", "us");

        Assert.Empty(store.GetMany([(_id, "/c/Book/Part 1.m4b")]));
    }

    [Fact]
    public async Task DamagedFile_StartsOverAndKeepsTheOldOne()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "files.json");
        await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);

        Assert.Null(NewStore().Get(_id, Acorna));
        Assert.True(File.Exists(path + ".bad"));
    }
}
