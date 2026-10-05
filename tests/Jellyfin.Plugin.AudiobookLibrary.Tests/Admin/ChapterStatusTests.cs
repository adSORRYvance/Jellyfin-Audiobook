using Jellyfin.Plugin.AudiobookLibrary.Admin;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Admin;

public class ChapterStatusTests
{
    private static readonly double[] None = [];
    private static readonly double[] Placeholder = [0];
    private static readonly double[] Three = [0, 600, 1800];

    private static FileRecord Applied(string source, int count) => new(Guid.NewGuid(), "/b.m4b", null, null, source, DateTimeOffset.UnixEpoch, count);

    [Fact]
    public void Mp3_IsNotSupported()
    {
        Assert.Equal(FileChapterStatus.NotSupported, ChapterStatus.Classify(false, Three, null, false));
    }

    [Fact]
    public void NoChaptersOrALonePlaceholder_IsNoChapters()
    {
        Assert.Equal(FileChapterStatus.NoChapters, ChapterStatus.Classify(true, None, null, false));
        Assert.Equal(FileChapterStatus.NoChapters, ChapterStatus.Classify(true, Placeholder, null, false));
    }

    [Fact]
    public void RealChaptersWeDidntWrite_AreEmbedded()
    {
        Assert.Equal(FileChapterStatus.Embedded, ChapterStatus.Classify(true, Three, null, false));
    }

    [Fact]
    public void ChaptersWeWrote_ShowTheirSource()
    {
        Assert.Equal(FileChapterStatus.Audible, ChapterStatus.Classify(true, Three, Applied("Audible", 3), true));
        Assert.Equal(FileChapterStatus.Silence, ChapterStatus.Classify(true, Three, Applied("Silence", 3), true));
    }

    [Fact]
    public void BeforeJellyfinReReadsTheFile_TheOldCountDoesntMeanReview()
    {
        Assert.Equal(FileChapterStatus.Silence, ChapterStatus.Classify(true, Placeholder, Applied("Silence", 49), false));
    }

    [Fact]
    public void CountChangedAfterJellyfinReReadIt_NeedsReview()
    {
        Assert.Equal(FileChapterStatus.NeedsReview, ChapterStatus.Classify(true, Placeholder, Applied("Silence", 49), true));
    }

    [Fact]
    public void AnAsinAlone_DoesntChangeTheStatus()
    {
        var record = new FileRecord(Guid.NewGuid(), "/b.m4b", "B09N446YK7", "us", null, null, null);

        Assert.Equal(FileChapterStatus.NoChapters, ChapterStatus.Classify(true, Placeholder, record, true));
    }
}
