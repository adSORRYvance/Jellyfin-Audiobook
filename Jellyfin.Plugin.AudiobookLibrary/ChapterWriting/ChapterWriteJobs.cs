using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We run chapter writes in the background, one at a time, and keep each file's last result for the admin page to poll.
/// A big book takes a minute between the rewrite and two hash passes, too long to hold a request open.
/// </summary>
public sealed partial class ChapterWriteJobs : IDisposable
{
    private readonly M4bChapterWriter _writer;
    private readonly IItemRefresher _refresher;
    private readonly TimeProvider _time;
    private readonly ILogger<ChapterWriteJobs> _logger;

    // Rewriting files is heavy on the disk, and one at a time keeps two writes from ever touching the same folder at once
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ChapterWriteJob> _jobs = new();
    private readonly Lock _startLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterWriteJobs"/> class.
    /// </summary>
    /// <param name="writer">Does the writing.</param>
    /// <param name="refresher">Tells Jellyfin a file changed.</param>
    /// <param name="time">The clock, so tests get fixed times.</param>
    /// <param name="logger">Logger.</param>
    public ChapterWriteJobs(M4bChapterWriter writer, IItemRefresher refresher, TimeProvider time, ILogger<ChapterWriteJobs> logger)
    {
        _writer = writer;
        _refresher = refresher;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Starts writing chapters, or returns the write already waiting or running for that file.
    /// </summary>
    /// <param name="target">The file and its new chapters.</param>
    /// <returns>The write's state right away, the write carries on in the background.</returns>
    public ChapterWriteStatus Start(ChapterWriteTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The lock makes two quick Start calls share one job instead of racing to make two
        lock (_startLock)
        {
            if (_jobs.TryGetValue(target.ItemId, out var existing) && existing.IsActive)
            {
                return existing.Snapshot();
            }

            var job = new ChapterWriteJob(target);
            _jobs[target.ItemId] = job;
            existing?.Dispose();
            job.Run = Task.Run(() => RunAsync(job));
            return job.Snapshot();
        }
    }

    /// <summary>
    /// Gets the last write's state for a file.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <returns>The state, or null when nothing was written since Jellyfin started.</returns>
    public ChapterWriteStatus? Get(Guid itemId) => _jobs.TryGetValue(itemId, out var job) ? job.Snapshot() : null;

    /// <summary>
    /// Puts a file's original back from its backup and refreshes it.
    /// </summary>
    /// <param name="itemId">The file's item.</param>
    /// <param name="path">The file on disk.</param>
    /// <returns>Why it couldn't be restored, or null when it was.</returns>
    public string? Restore(Guid itemId, string path)
    {
        // Under the same lock as Start, so a restore can't land in the middle of a write to the same file
        lock (_startLock)
        {
            if (_jobs.TryGetValue(itemId, out var job) && job.IsActive)
            {
                return "Chapters are being written to this file, try again once that finishes";
            }

            try
            {
                _writer.Restore(path);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }

        _refresher.Refresh(itemId);
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Shutdown stops writes that haven't reached the swap, a swap already running finishes first
        var jobs = _jobs.Values.ToList();
        jobs.ForEach(j => j.Cancel());
        Task.WaitAll(jobs.Select(j => j.Run).OfType<Task>().ToArray(), TimeSpan.FromSeconds(10));
        jobs.ForEach(j => j.Dispose());
        _oneAtATime.Dispose();
    }

    private async Task RunAsync(ChapterWriteJob job)
    {
        var target = job.Target;
        var entered = false;
        try
        {
            await _oneAtATime.WaitAsync(job.Token).ConfigureAwait(false);
            entered = true;

            await _writer.WriteAsync(target.Path, target.Chapters, job.SetStage, job.Token).ConfigureAwait(false);

            job.SetStage(ChapterWriteStage.Refreshing);
            _refresher.Refresh(target.ItemId);
            job.SetDone(_time.GetUtcNow());
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            job.SetFailed("Jellyfin shut down before the write finished, the original is untouched", _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or FormatException)
        {
            job.SetFailed(ex.Message, _time.GetUtcNow());
            LogFailed(ex, target.Path);
        }
        finally
        {
            if (entered)
            {
                _oneAtATime.Release();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing chapters into {Path} failed, the original is untouched")]
    private partial void LogFailed(Exception ex, string path);
}
