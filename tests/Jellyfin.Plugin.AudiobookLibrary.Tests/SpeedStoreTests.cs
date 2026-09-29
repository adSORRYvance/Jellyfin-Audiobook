using Jellyfin.Plugin.AudiobookLibrary.Preferences;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

public sealed class SpeedStoreTests : IDisposable
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();
    private static readonly Guid BookA = Guid.NewGuid();
    private static readonly Guid BookB = Guid.NewGuid();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-speeds-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private SpeedStore NewStore() => new(_folder, NullLogger<SpeedStore>.Instance);

    [Fact]
    public void Get_NothingSaved_ReturnsDefault()
    {
        Assert.Equal(1.0, NewStore().Get(UserA, BookA));
    }

    [Fact]
    public void Set_ThenGetFromANewStore_ReturnsSavedSpeed()
    {
        // A new store reads from disk, the same as after a server restart
        NewStore().Set(UserA, BookA, 1.3, "The Well of Ascension");

        Assert.Equal(1.3, NewStore().Get(UserA, BookA));
    }

    [Fact]
    public void Set_OneBook_LeavesOtherBooksAlone()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 1.3, "Book A");

        Assert.Equal(1.0, store.Get(UserA, BookB));
    }

    [Fact]
    public void Set_SameBookTwoUsers_KeepsEachUsersSpeed()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 1.3, "Book A");
        store.Set(UserB, BookA, 0.8, "Book A");

        Assert.Equal(1.3, store.Get(UserA, BookA));
        Assert.Equal(0.8, store.Get(UserB, BookA));
    }

    [Fact]
    public void Set_Again_ReplacesTheSpeed()
    {
        var store = NewStore();
        store.Set(UserA, BookA, 1.3, "Book A");
        store.Set(UserA, BookA, 2.0, "Book A");

        Assert.Equal(2.0, store.Get(UserA, BookA));
    }

    [Fact]
    public void Get_DamagedFile_StartsOverAndKeepsTheOldFile()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, UserA.ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");

        var store = NewStore();

        Assert.Equal(1.0, store.Get(UserA, BookA));
        Assert.True(File.Exists(path + ".bad"));

        store.Set(UserA, BookA, 1.5, "Book A");
        Assert.Equal(1.5, store.Get(UserA, BookA));
    }

    [Theory]
    [InlineData(1.3, 1.3)]
    [InlineData(1.25, 1.3)]
    [InlineData(1.24, 1.2)]
    [InlineData(0.5, 0.5)]
    [InlineData(3.0, 3.0)]
    public void TryNormalize_InRange_RoundsToTenths(double speed, double expected)
    {
        Assert.True(SpeedStore.TryNormalize(speed, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(3.1)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TryNormalize_OutOfRange_Refuses(double speed)
    {
        Assert.False(SpeedStore.TryNormalize(speed, out _));
    }
}
