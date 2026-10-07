using Jellyfin.Plugin.AudiobookLibrary.Preferences;
using Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

public sealed class PositionStoreTests : IDisposable
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();
    private static readonly Guid BookA = Guid.NewGuid();
    private static readonly Guid BookB = Guid.NewGuid();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-positions-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private PositionStore NewStore() => new(_folder, _clock, NullLogger<PositionStore>.Instance);

    [Fact]
    public void Get_NothingSaved_IsNull()
    {
        Assert.Null(NewStore().Get(UserA, BookA));
    }

    [Fact]
    public void Set_ThenGetFromANewStore_ReturnsThePlaceAndTime()
    {
        // A new store reads from disk, the same as after a server restart
        NewStore().Set(UserA, BookA, 32400.5, "The Well of Ascension");

        Assert.Equal(new SavedPosition(32400.5, _clock.Now, "The Well of Ascension"), NewStore().Get(UserA, BookA));
    }

    [Fact]
    public void Set_OneBook_LeavesOtherBooksAlone()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 600, "Book A");

        Assert.Null(store.Get(UserA, BookB));
    }

    [Fact]
    public void Set_SameBookTwoUsers_KeepsEachUsersPlace()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 600, "Book A");
        store.Set(UserB, BookA, 13860, "Book A");

        Assert.Equal(600, store.Get(UserA, BookA)?.PositionSec);
        Assert.Equal(13860, store.Get(UserB, BookA)?.PositionSec);
    }

    [Fact]
    public void Set_Again_ReplacesThePlaceAndTime()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 600, "Book A");
        _clock.Now = _clock.Now.AddMinutes(1);

        var saved = store.Set(UserA, BookA, 615, "Book A");

        Assert.Equal(saved, store.Get(UserA, BookA));
        Assert.Equal(_clock.Now, saved.UpdatedAt);
    }

    [Fact]
    public void Get_DamagedFile_StartsOverAndKeepsTheOldFile()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, UserA.ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");

        var store = NewStore();

        Assert.Null(store.Get(UserA, BookA));
        Assert.True(File.Exists(path + ".bad"));

        store.Set(UserA, BookA, 42, "Book A");
        Assert.Equal(42, store.Get(UserA, BookA)?.PositionSec);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(32400.04, 32400)]
    [InlineData(32400.05, 32400.1)]
    public void TryNormalize_Valid_RoundsToTenths(double position, double expected)
    {
        Assert.True(PositionStore.TryNormalize(position, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TryNormalize_Invalid_Refuses(double position)
    {
        Assert.False(PositionStore.TryNormalize(position, out _));
    }
}
