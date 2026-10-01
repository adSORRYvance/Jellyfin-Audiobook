using Jellyfin.Plugin.AudiobookLibrary.Silences;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Silences;

public class BreakPickerTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "Silences", "TestData");

    private static Silence Gap(double start, double duration) => new(start, start + duration, duration);

    private static List<double> Starts(IReadOnlyList<SilenceChapter> chapters) => chapters.Skip(1).Select(c => c.StartSec).ToList();

    [Fact]
    public void Pick_PlacesBreakHalfASecondBeforeTheVoiceReturns()
    {
        var chapters = BreakPicker.Pick([Gap(600, 3.7)], 3, 3600);

        Assert.Equal([new SilenceChapter("Chapter 1", 0), new SilenceChapter("Chapter 2", 603.2)], chapters);
    }

    [Fact]
    public void Pick_PauseAfterChapterTitle_IsNotASecondBreak()
    {
        // The Lost Metal: 3.7 s before the break, then "Chapter Five", then 2.6 s
        var chapters = BreakPicker.Pick([Gap(600, 3.7), Gap(605, 2.6)], 2, 3600);

        Assert.Equal([603.2], Starts(chapters));
    }

    [Fact]
    public void Pick_BreaksFurtherApartThanTheWindow_AreBothKept()
    {
        var chapters = BreakPicker.Pick([Gap(600, 3), Gap(640, 3)], 3, 3600);

        Assert.Equal([602.5, 642.5], Starts(chapters));
    }

    [Fact]
    public void Pick_ShortPauses_AreIgnored()
    {
        var chapters = BreakPicker.Pick([Gap(600, 2.9), Gap(900, 3)], 3, 3600);

        Assert.Equal([902.5], Starts(chapters));
    }

    [Fact]
    public void Pick_QuietStartAndEnd_DontMakeTinyChapters()
    {
        var chapters = BreakPicker.Pick([Gap(0, 5), Gap(1800, 4), Gap(3590, 10)], 3, 3600);

        Assert.Equal([1803.5], Starts(chapters));
    }

    [Fact]
    public void Pick_NoBreaks_IsOneChapter()
    {
        Assert.Equal([new SilenceChapter("Chapter 1", 0)], BreakPicker.Pick([], 3, 3600));
    }

    [Fact]
    public void Pick_NumbersChaptersInTimeOrder()
    {
        // The longest silence is picked first, the numbering still has to follow the book
        var chapters = BreakPicker.Pick([Gap(900, 3), Gap(300, 6), Gap(600, 4)], 3, 3600);

        Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4"], chapters.Select(c => c.Title));
        Assert.Equal([305.5, 603.5, 902.5], Starts(chapters));
    }

    [Fact]
    public async Task Pick_TheLostMetal_FindsTheRealChaptersWithFewFalseBreaks()
    {
        // Real ffmpeg output at -30 dB and the book's 93 real chapter starts, which match Audible to the millisecond
        var token = TestContext.Current.CancellationToken;
        var silences = SilenceParser.Parse(await File.ReadAllLinesAsync(Path.Combine(TestData, "lost-metal-silences.log"), token));
        var truth = (await File.ReadAllLinesAsync(Path.Combine(TestData, "lost-metal-chapters.txt"), token))
            .Select(l => double.Parse(l, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var picked = Starts(BreakPicker.Pick(silences, 3, 67619.66));

        var found = truth.Count(t => picked.Any(p => Math.Abs(p - t) <= 2));
        var falseBreaks = picked.Count(p => !truth.Any(t => Math.Abs(p - t) <= 2));
        Assert.True(found >= 85, $"found {found} of {truth.Count} real breaks");
        Assert.True(falseBreaks <= 5, $"{falseBreaks} false breaks");
    }
}
