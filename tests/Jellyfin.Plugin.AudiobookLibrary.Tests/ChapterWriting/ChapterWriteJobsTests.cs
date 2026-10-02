using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.ChapterWriting;

public sealed class ChapterWriteJobsTests : IDisposable
{
    private static readonly ChapterEntry[] Chapters = [new("Opening", 0), new("Chapter One", 600)];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-writejobs-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMediaTool _tool = new();
    private readonly FakeRefresher _refresher = new();
    private readonly FakeClock _clock = new();
    private readonly ChapterWriteJobs _jobs;
    private readonly ChapterWriteTarget _target;

    public ChapterWriteJobsTests()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "Book.m4b");
        File.WriteAllText(path, "original audio");
        _target = new ChapterWriteTarget(Guid.NewGuid(), path, Chapters);
        _jobs = new ChapterWriteJobs(new M4bChapterWriter(_tool, NullLogger<M4bChapterWriter>.Instance), _refresher, _clock, NullLogger<ChapterWriteJobs>.Instance);
    }

    public void Dispose()
    {
        _jobs.Dispose();
        Directory.Delete(_folder, true);
    }

    // The write runs on the thread pool, so the tests wait for the state they expect, up to 5 s
    private async Task<ChapterWriteStatus> WaitFor(ChapterWriteState state)
    {
        for (var i = 0; i < 500; i++)
        {
            if (_jobs.Get(_target.ItemId) is { } status && status.State == state)
            {
                return status;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"never reached {state}, last seen {_jobs.Get(_target.ItemId)?.State}");
    }

    [Fact]
    public async Task Start_WritesThenRefreshesJellyfin()
    {
        var started = _jobs.Start(_target);
        var done = await WaitFor(ChapterWriteState.Done);

        Assert.Equal(ChapterWriteState.Queued, started.State);
        Assert.Equal(_clock.Now, done.FinishedAt);
        Assert.Equal([_target.ItemId], _refresher.Refreshed);
        Assert.True(File.Exists(M4bChapterWriter.BackupPath(_target.Path)));
    }

    [Fact]
    public async Task FailedCheck_ReportsTheReason_AndDoesntRefresh()
    {
        _tool.CopyHash = "MD5=different";

        _jobs.Start(_target);
        var failed = await WaitFor(ChapterWriteState.Failed);

        Assert.Equal("The new file's audio doesn't match the original, the original is untouched", failed.Error);
        Assert.Empty(_refresher.Refreshed);
    }

    [Fact]
    public async Task Start_WhileRunning_SharesTheJob()
    {
        _tool.WriteGate = new TaskCompletionSource();
        _jobs.Start(_target);
        _jobs.Start(_target);
        _tool.WriteGate.SetResult();
        await WaitFor(ChapterWriteState.Done);

        Assert.Single(_tool.Calls, c => c.StartsWith("write", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_WhileWriting_IsRefused()
    {
        _tool.WriteGate = new TaskCompletionSource();
        _jobs.Start(_target);

        var problem = _jobs.Restore(_target.ItemId, _target.Path);

        _tool.WriteGate.SetResult();
        await WaitFor(ChapterWriteState.Done);
        Assert.Equal("Chapters are being written to this file, try again once that finishes", problem);
    }

    [Fact]
    public async Task Restore_AfterWrite_PutsTheOriginalBackAndRefreshes()
    {
        _jobs.Start(_target);
        await WaitFor(ChapterWriteState.Done);

        Assert.Null(_jobs.Restore(_target.ItemId, _target.Path));
        Assert.Equal("original audio", await File.ReadAllTextAsync(_target.Path, TestContext.Current.CancellationToken));
        Assert.Equal([_target.ItemId, _target.ItemId], _refresher.Refreshed);
    }

    [Fact]
    public void Get_NothingWritten_IsNull()
    {
        Assert.Null(_jobs.Get(_target.ItemId));
    }

    private sealed class FakeRefresher : IItemRefresher
    {
        public List<Guid> Refreshed { get; } = [];

        public void Refresh(Guid itemId) => Refreshed.Add(itemId);
    }
}
