using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

public class BookTimelineTests
{
    private static long Sec(double seconds) => (long)(seconds * TimeSpan.TicksPerSecond);

    private static OrderedTrack Track(string fileName, double lengthSec, string? title = null, params (string Name, double Start)[] chapters)
        => new(
            new TrackSource(Guid.NewGuid(), fileName, null, Sec(lengthSec), chapters.Select(c => new ChapterMark(c.Name, Sec(c.Start))).ToList()),
            title);

    [Fact]
    public void Build_SingleFileWithChapters_EndsAtNextStartAndBookEnd()
    {
        var book = BookTimeline.Build([Track("book.m4b", 100, null, ("Opening", 0), ("Prologue", 30), ("One", 60))]);

        Assert.Equal(Sec(100), book.DurationTicks);
        Assert.Equal(["Opening", "Prologue", "One"], book.Chapters.Select(c => c.Title));
        Assert.Equal([Sec(30), Sec(60), Sec(100)], book.Chapters.Select(c => c.EndTicks));
    }

    [Fact]
    public void Build_SingleFileWithoutChapters_HasNoChapters()
    {
        var book = BookTimeline.Build([Track("book.m4b", 100)]);

        Assert.Empty(book.Chapters);
        Assert.Single(book.Tracks);
    }

    [Fact]
    public void Build_PlaceholderChapterAtZero_CountsAsNone()
    {
        // Acorna and many other rips carry one Chapter_0 at 0 s
        var book = BookTimeline.Build([Track("acorna.m4b", 100, null, ("Chapter_0", 0))]);

        Assert.Empty(book.Chapters);
    }

    [Fact]
    public void Build_SplitM4bs_ShiftsSecondFileChapters()
    {
        var book = BookTimeline.Build(
        [
            Track("part1.m4b", 100, null, ("Chapter 1", 0), ("Chapter 2", 40)),
            Track("part2.m4b", 50, null, ("Chapter 1", 0), ("Chapter 2", 20)),
        ]);

        Assert.Equal(Sec(150), book.DurationTicks);
        Assert.Equal([Sec(0), Sec(40), Sec(100), Sec(120)], book.Chapters.Select(c => c.StartTicks));
        Assert.Equal([0, 0, 1, 1], book.Chapters.Select(c => c.TrackIndex));
        Assert.Equal([Sec(0), Sec(40), Sec(0), Sec(20)], book.Chapters.Select(c => c.TrackOffsetTicks));
        Assert.Equal(Sec(100), book.Tracks[1].StartTicks);
    }

    [Fact]
    public void Build_MultipleMp3sWithoutChapters_OneChapterPerFile()
    {
        var book = BookTimeline.Build(
        [
            Track("Opening.mp3", 6, "Opening"),
            Track("Chapter 1.mp3", 875),
            Track("Chapter 2.mp3", 1292),
        ]);

        Assert.Equal(["Opening", "Chapter 1", "Chapter 2"], book.Chapters.Select(c => c.Title));
        Assert.Equal([Sec(0), Sec(6), Sec(881)], book.Chapters.Select(c => c.StartTicks));
        Assert.Equal(Sec(2173), book.Chapters[^1].EndTicks);
    }

    [Fact]
    public void Build_MixedFiles_UseChaptersWhereTheyExist()
    {
        var book = BookTimeline.Build(
        [
            Track("intro.mp3", 10),
            Track("main.m4b", 100, null, ("A", 0), ("B", 50)),
        ]);

        Assert.Equal(["intro", "A", "B"], book.Chapters.Select(c => c.Title));
        Assert.Equal([Sec(0), Sec(10), Sec(60)], book.Chapters.Select(c => c.StartTicks));
    }

    [Fact]
    public void Build_BadMarks_AreSortedDeduplicatedAndClipped()
    {
        var book = BookTimeline.Build([Track("book.m4b", 100, null, ("B", 50), ("A", 0), ("A again", 0), ("Past end", 120), ("Negative", -5))]);

        Assert.Equal(["A", "B"], book.Chapters.Select(c => c.Title));
    }

    [Fact]
    public void Build_UnnamedChapter_GetsANumber()
    {
        var book = BookTimeline.Build([Track("book.m4b", 100, null, ("", 0), ("  ", 50))]);

        Assert.Equal(["Chapter 1", "Chapter 2"], book.Chapters.Select(c => c.Title));
    }

    [Fact]
    public void Build_NoTracks_IsEmpty()
    {
        var book = BookTimeline.Build([]);

        Assert.Equal(0, book.DurationTicks);
        Assert.Empty(book.Tracks);
        Assert.Empty(book.Chapters);
    }
}
