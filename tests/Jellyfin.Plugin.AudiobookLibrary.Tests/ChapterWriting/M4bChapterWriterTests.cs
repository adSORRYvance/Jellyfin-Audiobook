using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.ChapterWriting;

// Folder permissions are a Unix thing, and Jellyfin servers this targets run on Linux
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class M4bChapterWriterTests : IDisposable
{
    private const string Original = "original audio";

    private static readonly ChapterEntry[] Chapters = [new("Opening", 0), new("Chapter One", 600), new("Chapter Two", 1800)];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-write-" + Guid.NewGuid().ToString("N"));
    private readonly string _book;
    private readonly FakeMediaTool _tool = new();
    private readonly M4bChapterWriter _writer;

    public M4bChapterWriterTests()
    {
        Directory.CreateDirectory(_folder);
        _book = Path.Combine(_folder, "The Book.m4b");
        File.WriteAllText(_book, Original);
        _writer = new M4bChapterWriter(_tool, NullLogger<M4bChapterWriter>.Instance);
    }

    public void Dispose()
    {
        // A test may have locked the folder to check the permission error
        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(_folder, true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Backup => M4bChapterWriter.BackupPath(_book);

    private Task Write(IReadOnlyList<ChapterEntry>? chapters = null, CancellationToken? token = null, List<ChapterWriteStage>? stages = null)
        => _writer.WriteAsync(_book, chapters ?? Chapters, s => stages?.Add(s), token ?? Token);

    private void AssertUntouched()
    {
        Assert.Equal(Original, File.ReadAllText(_book));
        Assert.False(File.Exists(Backup));
        Assert.Equal(["The Book.m4b"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Write_SwapsInTheCopyAndKeepsTheOriginalAsBak()
    {
        var stages = new List<ChapterWriteStage>();

        await Write(stages: stages);

        var book = await File.ReadAllTextAsync(_book, Token);
        Assert.StartsWith("new\n", book, StringComparison.Ordinal);
        Assert.Contains("title=Chapter Two", book, StringComparison.Ordinal);
        Assert.Equal(Original, await File.ReadAllTextAsync(Backup, Token));
        Assert.False(File.Exists(M4bChapterWriter.TempPath(_book)));
        Assert.Equal([ChapterWriteStage.Preparing, ChapterWriteStage.Writing, ChapterWriteStage.Checking, ChapterWriteStage.Replacing], stages);
    }

    [Fact]
    public async Task Write_HashesTheOriginalBeforeAndTheCopyAfter()
    {
        await Write();

        Assert.Equal(
            ["probe The Book.m4b", "hash The Book.m4b", "write .The Book.m4b.abl-tmp", "probe .The Book.m4b.abl-tmp", "hash .The Book.m4b.abl-tmp"],
            _tool.Calls);
    }

    [Fact]
    public async Task Write_AudioChanged_LeavesTheOriginalUntouched()
    {
        _tool.CopyHash = "MD5=different";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write());

        Assert.Contains("audio doesn't match", ex.Message, StringComparison.Ordinal);
        AssertUntouched();
    }

    [Fact]
    public async Task Write_DurationChanged_LeavesTheOriginalUntouched()
    {
        _tool.CopyDurationSec = 3598;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write());

        Assert.Contains("3598.0 s long instead of 3600.0 s", ex.Message, StringComparison.Ordinal);
        AssertUntouched();
    }

    [Fact]
    public async Task Write_ChapterMissing_LeavesTheOriginalUntouched()
    {
        _tool.CopyDropsLastChapter = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write());

        Assert.Contains("2 chapters that don't match the 3", ex.Message, StringComparison.Ordinal);
        AssertUntouched();
    }

    [Fact]
    public async Task Write_TagLost_LeavesTheOriginalUntouched()
    {
        _tool.CopyLosesArtist = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write());

        Assert.Contains("lost its artist tag", ex.Message, StringComparison.Ordinal);
        AssertUntouched();
    }

    [Fact]
    public async Task Write_BadChapterList_IsRefusedBeforeWriting()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write([new ChapterEntry("One", 0), new ChapterEntry("Two", 4000)]));

        Assert.Equal("Chapter 2 starts after the end of the file", ex.Message);
        Assert.DoesNotContain(_tool.Calls, c => c.StartsWith("write", StringComparison.Ordinal));
        AssertUntouched();
    }

    [Fact]
    public async Task Write_Mp3_IsRefused()
    {
        var mp3 = Path.Combine(_folder, "Chapter 1.mp3");
        await File.WriteAllTextAsync(mp3, "mp3", Token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteAsync(mp3, Chapters, _ => { }, Token));

        Assert.Equal("Only .m4b and .m4a files can get chapters", ex.Message);
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Write_FolderNotWritable_SaysSoBeforeAnythingElse()
    {
        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Write());

        Assert.StartsWith($"Jellyfin can't write to {_folder}", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Write_CancelledBeforeTheSwap_LeavesTheOriginalUntouched()
    {
        using var cancel = new CancellationTokenSource();
        _tool.AfterCopyHashed = _ => cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(token: cancel.Token));

        AssertUntouched();
    }

    [Fact]
    public async Task Write_SwapFails_PutsTheOriginalBack()
    {
        // The copy vanishes after its checks pass, so moving it into place fails halfway through the swap
        _tool.AfterCopyHashed = File.Delete;

        await Assert.ThrowsAsync<FileNotFoundException>(() => Write());

        AssertUntouched();
    }

    [Fact]
    public async Task Write_Twice_KeepsTheFirstOriginalAsBak()
    {
        await Write();
        await Write([new ChapterEntry("Only One", 0)]);

        Assert.Equal(Original, await File.ReadAllTextAsync(Backup, Token));
        Assert.Contains("title=Only One", await File.ReadAllTextAsync(_book, Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_PutsTheOriginalBack()
    {
        await Write();

        _writer.Restore(_book);

        AssertUntouched();
    }

    [Fact]
    public void Restore_WithoutBak_SaysSo()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _writer.Restore(_book));

        Assert.StartsWith("There's no backup of", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllText(_book));
    }

    [Fact]
    public void TempPath_IsHiddenAndNotAnAudioExtension()
    {
        Assert.Equal(Path.Combine(_folder, ".The Book.m4b.abl-tmp"), M4bChapterWriter.TempPath(_book));
    }
}
