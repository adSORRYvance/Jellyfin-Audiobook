using Jellyfin.Plugin.AudiobookLibrary.Silences;
using Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Silences;

public sealed class SilenceJobsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-silences-" + Guid.NewGuid().ToString("N"));
    private readonly FakeScanner _scanner = new();
    private readonly FakeClock _clock = new();
    private readonly SilenceJobs _jobs;
    private readonly SilenceTarget _book;
    private readonly SilenceTarget _otherBook;

    public SilenceJobsTests()
    {
        Directory.CreateDirectory(_folder);
        _book = NewTarget("book.m4b");
        _otherBook = NewTarget("other.m4b");
        _jobs = NewJobs();
    }

    public void Dispose()
    {
        _jobs.Dispose();
        Directory.Delete(_folder, true);
    }

    private SilenceTarget NewTarget(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "not really audio");
        return new SilenceTarget(Guid.NewGuid(), path, 100, -30);
    }

    private SilenceJobs NewJobs()
        => new(_scanner, new SilenceCache(Path.Combine(_folder, "cache"), NullLogger<SilenceCache>.Instance), _clock, NullLogger<SilenceJobs>.Instance);

    // The job runs on the thread pool, so the tests wait for it to reach the state they expect
    // Up to 5 s, the first run after a build is slow while .NET loads everything
    private static async Task<SilenceJobStatus> WaitFor(Func<SilenceJobStatus?> read, SilenceJobState state)
    {
        for (var i = 0; i < 500; i++)
        {
            if (read() is { } status && status.State == state)
            {
                return status;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"never reached {state}, last seen {read()?.State}");
    }

    [Fact]
    public async Task Start_RunsInTheBackgroundAndSavesTheResult()
    {
        var started = _jobs.Start(_book);
        // Progress arrives a moment after Running, so wait for the fake's 30 s of 100
        var running = await WaitFor(() => _jobs.Get(_book) is { Percent: 30 } s ? s : null, SilenceJobState.Running);
        await _scanner.Finish(new Silence(10, 14, 4));
        var done = await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        Assert.Equal(SilenceJobState.Queued, started.State);
        Assert.Equal(30, running.Percent);
        Assert.Equal(100, done.Percent);
        Assert.Equal([new Silence(10, 14, 4)], done.Scan!.Silences);
        Assert.Equal(_clock.Now, done.Scan.ScannedAt);
    }

    [Fact]
    public void Get_NeverScanned_IsNull()
    {
        Assert.Null(_jobs.Get(_book));
    }

    [Fact]
    public async Task Start_Twice_SharesOneScan()
    {
        _jobs.Start(_book);
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        Assert.Equal(1, _scanner.Started);
    }

    [Fact]
    public async Task TwoBooks_OneScanAtATime()
    {
        // The second starts once the first holds the slot, otherwise either could go first
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        _jobs.Start(_otherBook);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(SilenceJobState.Queued, _jobs.Get(_otherBook)!.State);

        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_otherBook), SilenceJobState.Running);
        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_otherBook), SilenceJobState.Done);

        Assert.Equal(1, _scanner.MostAtOnce);
    }

    [Fact]
    public async Task Cancel_Running_StopsTheScan()
    {
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);

        Assert.True(_jobs.Cancel(_book.ItemId, _book.NoiseDb));
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Cancelled);
        Assert.False(_jobs.Cancel(_book.ItemId, _book.NoiseDb));
    }

    [Fact]
    public async Task Cancel_Queued_NeverStartsIt()
    {
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        _jobs.Start(_otherBook);

        _jobs.Cancel(_otherBook.ItemId, _otherBook.NoiseDb);
        await WaitFor(() => _jobs.Get(_otherBook), SilenceJobState.Cancelled);
        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        Assert.Equal(1, _scanner.Started);
    }

    [Fact]
    public async Task Failure_KeepsTheError_AndStartTriesAgain()
    {
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        await _scanner.Fail("ffmpeg stopped with exit code 1: Invalid data found when processing input");

        var failed = await WaitFor(() => _jobs.Get(_book), SilenceJobState.Failed);
        Assert.Equal("ffmpeg stopped with exit code 1: Invalid data found when processing input", failed.Error);

        // A job says Running a moment before it calls the scanner, so wait for the scan itself
        _jobs.Start(_book);
        for (var i = 0; i < 500 && _scanner.Started < 2; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, _scanner.Started);
    }

    [Fact]
    public async Task SavedScan_SurvivesARestart_AndIsNotScannedAgain()
    {
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        await _scanner.Finish(new Silence(10, 14, 4));
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        using var restarted = NewJobs();
        var status = restarted.Start(_book);

        Assert.Equal(SilenceJobState.Done, status.State);
        Assert.Equal(1, _scanner.Started);
    }

    [Fact]
    public async Task ChangedFile_IsScannedAgain()
    {
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        await File.AppendAllTextAsync(_book.Path, " with new chapters", TestContext.Current.CancellationToken);

        Assert.Null(_jobs.Get(_book));
        // A job says Running a moment before it calls the scanner, so wait for the scan itself
        _jobs.Start(_book);
        for (var i = 0; i < 500 && _scanner.Started < 2; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, _scanner.Started);
    }

    [Fact]
    public async Task EachLoudness_IsItsOwnScan()
    {
        var quieter = _book with { NoiseDb = -40 };
        _jobs.Start(_book);
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Running);
        await _scanner.Finish();
        await WaitFor(() => _jobs.Get(_book), SilenceJobState.Done);

        Assert.Null(_jobs.Get(quieter));
    }
}
