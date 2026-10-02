using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// One write's state. The background task changes it and polling requests read it, so every change goes through a lock.
/// </summary>
internal sealed class ChapterWriteJob : IDisposable
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private ChapterWriteStatus _status = new(ChapterWriteState.Queued, null, null, null);

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterWriteJob"/> class.
    /// </summary>
    /// <param name="target">What to write.</param>
    public ChapterWriteJob(ChapterWriteTarget target)
    {
        Target = target;
    }

    /// <summary>
    /// Gets what this job writes.
    /// </summary>
    public ChapterWriteTarget Target { get; }

    /// <summary>
    /// Gets the token that stops this job, used only at shutdown.
    /// </summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Gets or sets the background task running this job, so shutdown can wait for it.
    /// </summary>
    public Task? Run { get; set; }

    /// <summary>
    /// Gets a value indicating whether the job is still queued or running.
    /// </summary>
    public bool IsActive => Snapshot().State is ChapterWriteState.Queued or ChapterWriteState.Running;

    /// <summary>
    /// Records which step is running.
    /// </summary>
    /// <param name="stage">The step.</param>
    public void SetStage(ChapterWriteStage stage) => Set(new ChapterWriteStatus(ChapterWriteState.Running, stage, null, null));

    /// <summary>
    /// Marks the job as done.
    /// </summary>
    /// <param name="at">When it finished.</param>
    public void SetDone(DateTimeOffset at) => Set(new ChapterWriteStatus(ChapterWriteState.Done, null, null, at));

    /// <summary>
    /// Marks the job as failed.
    /// </summary>
    /// <param name="error">Why.</param>
    /// <param name="at">When it failed.</param>
    public void SetFailed(string error, DateTimeOffset at) => Set(new ChapterWriteStatus(ChapterWriteState.Failed, null, error, at));

    /// <summary>
    /// Stops the job before its swap, used at shutdown.
    /// </summary>
    public void Cancel() => _cancellation.Cancel();

    /// <summary>
    /// Gets the job's current state.
    /// </summary>
    /// <returns>A snapshot.</returns>
    public ChapterWriteStatus Snapshot()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cancellation.Dispose();

    private void Set(ChapterWriteStatus status)
    {
        lock (_lock)
        {
            _status = status;
        }
    }
}
