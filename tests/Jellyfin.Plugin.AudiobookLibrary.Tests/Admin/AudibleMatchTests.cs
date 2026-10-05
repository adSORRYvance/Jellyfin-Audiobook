using Jellyfin.Plugin.AudiobookLibrary.Admin;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Admin;

public class AudibleMatchTests
{
    // The Lost Metal's first chapters and runtime as Audnexus sends them
    private static AudibleChapters LostMetal(params AudibleChapter[] chapters) => new(
        "B09N446YK7",
        true,
        67619694,
        3924,
        4945,
        chapters.Length > 0 ? chapters : [new("Opening Credits", 0, 37543), new("Acknowledgments", 37543, 567243), new("Prologue", 604786, 1257221)],
        "us");

    [Fact]
    public void SameEdition_Matches_AndChaptersAreInSeconds()
    {
        // The file on jellyfin-test is 0.04 s shorter than Audible's runtime
        var result = AudibleMatch.Compare(LostMetal(), 67619.656667);

        Assert.True(result.Matches);
        Assert.Equal(-0.04, result.DifferenceSec);
        Assert.Equal([new ChapterEntry("Opening Credits", 0), new ChapterEntry("Acknowledgments", 37.543), new ChapterEntry("Prologue", 604.786)], result.Chapters);
    }

    [Fact]
    public void FewSecondsApart_DoesntMatch()
    {
        // A rip without Audible's 3.9 s intro puts every chapter that far off
        var result = AudibleMatch.Compare(LostMetal(), 67615.7);

        Assert.False(result.Matches);
        Assert.Equal(-3.99, result.DifferenceSec);
    }

    [Fact]
    public void ChaptersPastTheFileEnd_AreLeftOut()
    {
        var result = AudibleMatch.Compare(LostMetal(), 600);

        Assert.Equal(2, result.Chapters.Count);
        Assert.Equal(1, result.DroppedPastEnd);
    }

    [Fact]
    public void FirstChapter_AlwaysStartsAtZero_AndBlankTitlesGetNumbers()
    {
        var result = AudibleMatch.Compare(LostMetal(new AudibleChapter("Intro", 120, 1000), new AudibleChapter("  ", 5000, 1000)), 67619.7);

        Assert.Equal([new ChapterEntry("Intro", 0), new ChapterEntry("Chapter 2", 5)], result.Chapters);
    }

    [Fact]
    public void Result_PassesTheWritersChecks()
    {
        var result = AudibleMatch.Compare(LostMetal(), 67619.656667);

        Assert.Null(ChapterFile.Validate(result.Chapters, 67619.656667));
    }
}
