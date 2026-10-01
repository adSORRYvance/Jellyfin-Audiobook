using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// One scan's state while it waits or runs. The background task writes it and polling requests read it, so every change goes through a lock.
/// </summary>
internal sealed class SilenceJob : IDisposable
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private SilenceJobState _state = SilenceJobState.Queued;
    private double _percent;
    private string? _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="SilenceJob"/> class.
    /// </summary>
    /// <param name="target">What to scan.</param>
    public SilenceJob(SilenceTarget target)
    {
        Target = target;
    }

    /// <summary>
    /// Gets what this job scans.
    /// </summary>
    public SilenceTarget Target { get; }

    /// <summary>
    /// Gets the token that stops this job.
    /// </summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Gets or sets the background task running this job, so shutdown can wait for it.
    /// </summary>
    public Task? Run { get; set; }

    /// <summary>
    /// Gets a value indicating whether the job is still queued or running.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _state is SilenceJobState.Queued or SilenceJobState.Running;
            }
        }
    }

    /// <summary>
    /// Marks the job as running.
    /// </summary>
    public void SetRunning() => Set(SilenceJobState.Running, 0, null);

    /// <summary>
    /// Records how far ffmpeg has got.
    /// </summary>
    /// <param name="percent">Percent of the file read.</param>
    public void SetPercent(double percent)
    {
        lock (_lock)
        {
            // 100 is saved for Done, ffmpeg can report a little past the tagged runtime
            _percent = Math.Clamp(percent, 0, 99);
        }
    }

    /// <summary>
    /// Marks the job as failed.
    /// </summary>
    /// <param name="error">Why.</param>
    public void SetFailed(string error) => Set(SilenceJobState.Failed, null, error);

    /// <summary>
    /// Marks the job as stopped.
    /// </summary>
    public void SetCancelled() => Set(SilenceJobState.Cancelled, null, null);

    /// <summary>
    /// Stops the job, which kills ffmpeg when it's running.
    /// </summary>
    public void Cancel() => _cancellation.Cancel();

    /// <summary>
    /// Gets a copy of the job's state for a polling request.
    /// </summary>
    /// <returns>The state, percent and any error.</returns>
    public SilenceJobStatus Snapshot()
    {
        lock (_lock)
        {
            return new SilenceJobStatus(_state, Math.Round(_percent, 1), _error, null);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cancellation.Dispose();

    private void Set(SilenceJobState state, double? percent, string? error)
    {
        lock (_lock)
        {
            _state = state;
            _percent = percent ?? _percent;
            _error = error;
        }
    }
}
