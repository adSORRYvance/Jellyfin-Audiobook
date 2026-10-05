using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// We run silence scans in the background, one at a time, and keep their state for the admin page to poll.
/// Finished scans live in the cache, so this only tracks scans that are waiting, running, failed or stopped.
/// </summary>
public sealed partial class SilenceJobs : IDisposable
{
    private readonly ISilenceScanner _scanner;
    private readonly SilenceCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<SilenceJobs> _logger;

    // Decoding a book keeps a CPU core busy, and several at once would slow down anyone watching something
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ConcurrentDictionary<string, SilenceJob> _jobs = new();
    private readonly Lock _startLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SilenceJobs"/> class.
    /// </summary>
    /// <param name="scanner">Runs the scans.</param>
    /// <param name="cache">Where finished scans are saved.</param>
    /// <param name="time">The clock, so tests get fixed times.</param>
    /// <param name="logger">Logger.</param>
    public SilenceJobs(ISilenceScanner scanner, SilenceCache cache, TimeProvider time, ILogger<SilenceJobs> logger)
    {
        _scanner = scanner;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Starts a scan, or returns the one already waiting or running, or the saved result when the file hasn't changed.
    /// </summary>
    /// <param name="target">What to scan.</param>
    /// <returns>The scan's state right away, the scan itself carries on in the background.</returns>
    public SilenceJobStatus Start(SilenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var key = Key(target.ItemId, target.NoiseDb);

        // The lock makes two quick Start calls share one job instead of racing to make two
        lock (_startLock)
        {
            if (_jobs.TryGetValue(key, out var existing) && existing.IsActive)
            {
                return existing.Snapshot();
            }

            var saved = ReadCurrent(target);
            if (saved is not null)
            {
                return Done(saved);
            }

            var job = new SilenceJob(target);
            _jobs[key] = job;
            existing?.Dispose();

            // Read before starting, the background task can be past Queued by the time Task.Run returns
            var status = job.Snapshot();
            job.Run = Task.Run(() => RunAsync(key, job));
            return status;
        }
    }

    /// <summary>
    /// Gets a scan's state.
    /// </summary>
    /// <param name="target">The file and loudness.</param>
    /// <returns>The state, or null when it was never scanned at this loudness or the file changed since.</returns>
    public SilenceJobStatus? Get(SilenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_jobs.TryGetValue(Key(target.ItemId, target.NoiseDb), out var job))
        {
            return job.Snapshot();
        }

        var saved = ReadCurrent(target);
        return saved is null ? null : Done(saved);
    }

    /// <summary>
    /// Stops a scan that is waiting or running.
    /// </summary>
    /// <param name="itemId">The item being scanned.</param>
    /// <param name="noiseDb">The loudness it's being scanned at.</param>
    /// <returns>False when there was nothing to stop.</returns>
    public bool Cancel(Guid itemId, int noiseDb)
    {
        if (_jobs.TryGetValue(Key(itemId, noiseDb), out var job) && job.IsActive)
        {
            job.Cancel();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Jellyfin is shutting down, so ffmpeg has to go too
        var jobs = _jobs.Values.ToList();
        jobs.ForEach(j => j.Cancel());
        Task.WaitAll(jobs.Select(j => j.Run).OfType<Task>().ToArray(), TimeSpan.FromSeconds(5));
        jobs.ForEach(j => j.Dispose());
        _oneAtATime.Dispose();
    }

    private static string Key(Guid itemId, int noiseDb) => string.Create(CultureInfo.InvariantCulture, $"{itemId:N}.{noiseDb}");

    private static SilenceJobStatus Done(SilenceScan scan) => new(SilenceJobState.Done, 100, null, scan);

    // A saved scan only counts while the file is the same one we scanned
    private SilenceScan? ReadCurrent(SilenceTarget target)
    {
        var scan = _cache.Read(target.ItemId, target.NoiseDb);
        if (scan is null)
        {
            return null;
        }

        var file = new FileInfo(target.Path);
        return file.Exists && file.Length == scan.FileSize && file.LastWriteTimeUtc == scan.FileModifiedUtc && scan.Path == target.Path
            ? scan
            : null;
    }

    private async Task RunAsync(string key, SilenceJob job)
    {
        var target = job.Target;
        var entered = false;
        try
        {
            await _oneAtATime.WaitAsync(job.Token).ConfigureAwait(false);
            entered = true;
            job.SetRunning();

            // Read before the scan, so a file replaced halfway through won't match what we save
            var file = new FileInfo(target.Path);
            var size = file.Length;
            var modified = file.LastWriteTimeUtc;

            var silences = await _scanner.ScanAsync(
                target.Path,
                target.NoiseDb,
                seconds => job.SetPercent(target.DurationSec > 0 ? seconds / target.DurationSec * 100 : 0),
                job.Token).ConfigureAwait(false);

            _cache.Write(new SilenceScan(target.ItemId, target.Path, size, modified, target.NoiseDb, target.DurationSec, _time.GetUtcNow(), silences));
            LogFinished(target.Path, silences.Count);

            // From here on Get reads the saved scan
            if (_jobs.TryRemove(new(key, job)))
            {
                job.Dispose();
            }
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            job.SetCancelled();
            LogCancelled(target.Path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            job.SetFailed(ex.Message);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Silence scan of {Path} found {Count} silences")]
    private partial void LogFinished(string path, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Silence scan of {Path} was stopped")]
    private partial void LogCancelled(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Silence scan of {Path} failed")]
    private partial void LogFailed(Exception ex, string path);
}
