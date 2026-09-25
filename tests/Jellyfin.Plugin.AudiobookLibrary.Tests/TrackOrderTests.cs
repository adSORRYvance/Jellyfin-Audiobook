using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

public class TrackOrderTests
{
    private static TrackSource Track(string fileName, int? index = null)
        => new(Guid.NewGuid(), fileName, index, TimeSpan.TicksPerMinute, []);

    private static List<string> Names(IReadOnlyList<OrderedTrack> ordered)
        => ordered.Select(t => t.Source.FileName).ToList();

    [Fact]
    public void Sort_CleanTags_OrdersByTag()
    {
        // The Final Empire: tags are right, and file names would put Intro and Prologue in the wrong places
        var tracks = new[] { Track("Prologue.mp3", 2), Track("Chapter 1.mp3", 3), Track("Intro.mp3", 1), Track("Epilogue.mp3", 4) };

        var ordered = TrackOrder.Sort(tracks, null);

        Assert.Equal(["Intro.mp3", "Prologue.mp3", "Chapter 1.mp3", "Epilogue.mp3"], Names(ordered));
    }

    [Fact]
    public void Sort_TagsRestartEachPart_FallsBackToFileNames()
    {
        // GraphicAudio: each part restarts at track 1, so sorting by tag would mix the parts together
        var tracks = new[] { Track("[02-01].mp3", 1), Track("[01-02].mp3", 2), Track("[01-01].mp3", 1), Track("[02-02].mp3", 2) };

        var ordered = TrackOrder.Sort(tracks, null);

        Assert.Equal(["[01-01].mp3", "[01-02].mp3", "[02-01].mp3", "[02-02].mp3"], Names(ordered));
    }

    [Fact]
    public void Sort_MissingTags_UsesNaturalFileNameOrder()
    {
        var tracks = new[] { Track("Chapter 10.mp3"), Track("Chapter 2.mp3"), Track("Chapter 1.mp3") };

        var ordered = TrackOrder.Sort(tracks, null);

        Assert.Equal(["Chapter 1.mp3", "Chapter 2.mp3", "Chapter 10.mp3"], Names(ordered));
    }

    [Fact]
    public void Sort_TagsWithGap_UsesFileNames()
    {
        var tracks = new[] { Track("b.mp3", 1), Track("a.mp3", 3) };

        var ordered = TrackOrder.Sort(tracks, null);

        Assert.Equal(["a.mp3", "b.mp3"], Names(ordered));
    }

    [Fact]
    public void Sort_Playlist_WinsOverTagsAndGivesTitles()
    {
        var tracks = new[] { Track("Opening.mp3", 1), Track("Chapter 1.mp3", 2), Track("Closing.mp3", 3) };
        var playlist = new[]
        {
            new PlaylistEntry("Closing.mp3", "Closing"),
            new PlaylistEntry("Opening.mp3", "Opening"),
            new PlaylistEntry("Chapter 1.mp3", "Chapter 1"),
        };

        var ordered = TrackOrder.Sort(tracks, playlist);

        Assert.Equal(["Closing.mp3", "Opening.mp3", "Chapter 1.mp3"], Names(ordered));
        Assert.Equal(["Closing", "Opening", "Chapter 1"], ordered.Select(t => t.Title));
    }

    [Fact]
    public void Sort_PlaylistPaths_MatchIgnoringCaseSlashesAndEncoding()
    {
        var tracks = new[] { Track("Chapter 1.mp3"), Track("Chapter 2.mp3"), Track("Chapter 3.mp3") };
        var playlist = new[]
        {
            new PlaylistEntry("C:\\Books\\chapter 3.MP3", null),
            new PlaylistEntry("disc/Chapter%202.mp3", null),
            new PlaylistEntry("Chapter 1.mp3", null),
        };

        var ordered = TrackOrder.Sort(tracks, playlist);

        Assert.Equal(["Chapter 3.mp3", "Chapter 2.mp3", "Chapter 1.mp3"], Names(ordered));
    }

    [Fact]
    public void Sort_FilesMissingFromPlaylist_GoAtTheEnd()
    {
        var tracks = new[] { Track("Bonus 2.mp3"), Track("Chapter 1.mp3"), Track("Bonus 1.mp3") };
        var playlist = new[] { new PlaylistEntry("Chapter 1.mp3", "Chapter 1"), new PlaylistEntry("Deleted.mp3", "Gone") };

        var ordered = TrackOrder.Sort(tracks, playlist);

        Assert.Equal(["Chapter 1.mp3", "Bonus 1.mp3", "Bonus 2.mp3"], Names(ordered));
        Assert.Null(ordered[1].Title);
    }

    [Theory]
    [InlineData("Chapter 2", "Chapter 10")]
    [InlineData("Chapter 02", "Chapter 3")]
    [InlineData("a", "B")]
    [InlineData("Mistborn 2 - The Well of Ascension (1)", "Mistborn 2 - The Well of Ascension (2)")]
    [InlineData("Part 1", "Part 1 extra")]
    [InlineData("track 99999999999999999999", "track 100000000000000000000")]
    public void NaturalComparer_OrdersLikeAPerson(string first, string second)
    {
        Assert.True(NaturalStringComparer.Instance.Compare(first, second) < 0);
        Assert.True(NaturalStringComparer.Instance.Compare(second, first) > 0);
    }

    [Fact]
    public void NaturalComparer_ZeroPaddingOnly_IsStillOrdered()
    {
        Assert.NotEqual(0, NaturalStringComparer.Instance.Compare("1.mp3", "01.mp3"));
    }
}
