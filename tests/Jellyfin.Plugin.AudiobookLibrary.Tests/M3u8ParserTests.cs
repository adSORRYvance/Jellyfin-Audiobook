using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

public class M3u8ParserTests
{
    [Fact]
    public void Parse_TheyBloomAtNight_KeepsOrderAndRemovesAuthor()
    {
        var text = "#EXTM3U\n"
            + "#EXTINF:6,Trang Thanh Tran - Opening\nOpening.mp3\n"
            + "#EXTINF:875,Trang Thanh Tran - Chapter 1\nChapter 1.mp3\n"
            + "#EXTINF:1292,Trang Thanh Tran - Chapter 2\nChapter 2.mp3\n";

        var entries = M3u8Parser.Parse(text);

        Assert.Equal(["Opening.mp3", "Chapter 1.mp3", "Chapter 2.mp3"], entries.Select(e => e.Path));
        Assert.Equal(["Opening", "Chapter 1", "Chapter 2"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Parse_CrlfAndBom_AreIgnored()
    {
        var text = "\uFEFF#EXTM3U\r\n#EXTINF:10,One\r\na.mp3\r\n\r\n#EXTINF:10,Two\r\nb.mp3\r\n";

        var entries = M3u8Parser.Parse(text);

        Assert.Equal(["a.mp3", "b.mp3"], entries.Select(e => e.Path));
        Assert.Equal(["One", "Two"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Parse_EntryWithoutExtinf_HasNoTitle()
    {
        var entries = M3u8Parser.Parse("#EXTM3U\n#EXTINF:10,Named\na.mp3\nb.mp3\n");

        Assert.Equal("Named", entries[0].Title);
        Assert.Null(entries[1].Title);
    }

    [Fact]
    public void Parse_OtherTags_AreSkipped()
    {
        var entries = M3u8Parser.Parse("#EXTM3U\n#PLAYLIST:Book\n#EXTINF:10,One\n#EXTGRP:x\na.mp3\n");

        Assert.Single(entries);
        Assert.Equal("One", entries[0].Title);
    }

    [Fact]
    public void Parse_TitlesWithDifferentPrefixes_AreKept()
    {
        var entries = M3u8Parser.Parse("#EXTINF:10,Part 1 - The Beginning\na.mp3\n#EXTINF:10,Part 2 - The End\nb.mp3\n");

        Assert.Equal(["Part 1 - The Beginning", "Part 2 - The End"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Parse_SingleEntry_KeepsFullTitle()
    {
        var entries = M3u8Parser.Parse("#EXTINF:10,Author - Whole Book\nbook.mp3\n");

        Assert.Equal("Author - Whole Book", entries[0].Title);
    }

    [Fact]
    public void Parse_TitleThatIsOnlyThePrefix_KeepsEveryTitle()
    {
        var entries = M3u8Parser.Parse("#EXTINF:10,Author - One\na.mp3\n#EXTINF:10,Author - \nb.mp3\n");

        Assert.Equal(["Author - One", "Author -"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Parse_EmptyTitleAfterComma_IsNull()
    {
        var entries = M3u8Parser.Parse("#EXTINF:10,\na.mp3\n");

        Assert.Null(entries[0].Title);
    }

    [Fact]
    public void Parse_NoFiles_ReturnsEmpty()
    {
        Assert.Empty(M3u8Parser.Parse("#EXTM3U\n\n"));
    }

    [Theory]
    [InlineData("Chapter 1.mp3", "Chapter 1.mp3")]
    [InlineData("disc1/Chapter 1.mp3", "Chapter 1.mp3")]
    [InlineData("C:\\Books\\Chapter 1.mp3", "Chapter 1.mp3")]
    [InlineData("/books/Author/Book/Chapter 1.mp3", "Chapter 1.mp3")]
    public void FileNameOf_AnySlashStyle_ReturnsLastSegment(string path, string expected)
    {
        Assert.Equal(expected, M3u8Parser.FileNameOf(path));
    }
}
