using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.ChapterWriting;

public class ChapterFileTests
{
    [Fact]
    public void Build_WritesMillisecondsAndEndsEachChapterAtTheNext()
    {
        var text = ChapterFile.Build([new ChapterEntry("Opening Credits", 0), new ChapterEntry("Prologue", 37.543)], 120.5);

        Assert.Equal(
            ";FFMETADATA1\n"
            + "[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=37543\ntitle=Opening Credits\n"
            + "[CHAPTER]\nTIMEBASE=1/1000\nSTART=37543\nEND=120500\ntitle=Prologue\n",
            text);
    }

    [Fact]
    public void Build_EscapesCharactersThatMeanSomethingToFfmpeg()
    {
        var text = ChapterFile.Build([new ChapterEntry("Part One; A = B # C \\ D", 0)], 10);

        Assert.Contains("title=Part One\\; A \\= B \\# C \\\\ D\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_KeepsAccentsAndOtherScripts()
    {
        var text = ChapterFile.Build([new ChapterEntry("Épilogue — “quotes” 第三章", 0)], 10);

        Assert.Contains("title=Épilogue — “quotes” 第三章\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_GoodList_IsFine()
    {
        Assert.Null(ChapterFile.Validate([new ChapterEntry("One", 0), new ChapterEntry("Two", 60)], 120));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("not at 0")]
    [InlineData("out of order")]
    [InlineData("same start")]
    [InlineData("past the end")]
    [InlineData("blank title")]
    public void Validate_BadList_SaysWhy(string problem)
    {
        IReadOnlyList<ChapterEntry> chapters = problem switch
        {
            "empty" => [],
            "not at 0" => [new ChapterEntry("One", 5)],
            "out of order" => [new ChapterEntry("One", 0), new ChapterEntry("Two", 60), new ChapterEntry("Three", 30)],
            "same start" => [new ChapterEntry("One", 0), new ChapterEntry("Two", 0)],
            "past the end" => [new ChapterEntry("One", 0), new ChapterEntry("Two", 120)],
            _ => [new ChapterEntry("One", 0), new ChapterEntry(" ", 60)]
        };

        Assert.NotNull(ChapterFile.Validate(chapters, 120));
    }

    [Fact]
    public void Validate_Null_SaysWhy()
    {
        Assert.Equal("The chapter list is empty", ChapterFile.Validate(null, 120));
    }
}
