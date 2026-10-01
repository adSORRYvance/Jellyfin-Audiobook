using Jellyfin.Plugin.AudiobookLibrary.Silences;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Silences;

// Stands in for ffmpeg: each scan waits until the test lets it finish, fail or be cancelled
internal sealed class FakeScanner : ISilenceScanner
{
    private readonly object _lock = new();
    private readonly List<TaskCompletionSource<IReadOnlyList<Silence>>> _pending = [];

    public int Started { get; private set; }

    public int Running { get; private set; }

    public int MostAtOnce { get; private set; }

    public async Task<IReadOnlyList<Silence>> ScanAsync(string path, int noiseDb, Action<double> onProgress, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<IReadOnlyList<Silence>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            Started++;
            Running++;
            MostAtOnce = Math.Max(MostAtOnce, Running);
            _pending.Add(done);
        }

        try
        {
            onProgress(30);
            using var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
            return await done.Task;
        }
        finally
        {
            lock (_lock)
            {
                Running--;
            }
        }
    }

    public async Task Finish(params Silence[] silences) => (await Next()).TrySetResult(silences);

    public async Task Fail(string message) => (await Next()).TrySetException(new InvalidOperationException(message));

    // A job reports Running just before it calls the scanner, so the scan may not have arrived yet
    private async Task<TaskCompletionSource<IReadOnlyList<Silence>>> Next()
    {
        for (var i = 0; i < 500; i++)
        {
            lock (_lock)
            {
                var next = _pending.FirstOrDefault(p => !p.Task.IsCompleted);
                if (next is not null)
                {
                    return next;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("no scan arrived");
    }
}
