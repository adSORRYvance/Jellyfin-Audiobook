using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudiobookLibrary.Admin;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using Jellyfin.Plugin.AudiobookLibrary.Silences;
using MediaBrowser.Controller.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// Everything the admin page does: list books, match ASINs, preview chapters from Audible or silences, apply and restore.
/// Admin only, because it rewrites library files and makes outside requests on the server's behalf.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AudiobookLibrary/Admin")]
public class AdminController : ControllerBase
{
    private static readonly string[] _sources = [nameof(FileChapterStatus.Audible), nameof(FileChapterStatus.Silence)];

    private readonly AdminLibrary _library;
    private readonly FileRecordStore _records;
    private readonly AudnexusClient _audnexus;
    private readonly SilenceJobs _silences;
    private readonly ChapterWriteJobs _writes;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="library">Lists and finds audiobook files.</param>
    /// <param name="records">Each file's ASIN and chapter source.</param>
    /// <param name="audnexus">Audible lookups.</param>
    /// <param name="silences">Silence scans.</param>
    /// <param name="writes">Chapter writes.</param>
    public AdminController(AdminLibrary library, FileRecordStore records, AudnexusClient audnexus, SilenceJobs silences, ChapterWriteJobs writes)
    {
        _library = library;
        _records = records;
        _audnexus = audnexus;
        _silences = silences;
        _writes = writes;
    }

    private static Configuration.PluginConfiguration Config => Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

    /// <summary>
    /// Lists every audiobook file, grouped into books, with each file's chapter status.
    /// </summary>
    /// <returns>The books.</returns>
    [HttpGet("Books")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AdminBookDto>> GetBooks() => Ok(_library.GetBooks());

    /// <summary>
    /// Gets one file's row, for refreshing it after a change.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>The file.</returns>
    [HttpGet("Files/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AdminFileDto> GetFile([FromRoute] Guid itemId)
    {
        var file = _library.FindFile(itemId);
        return file is null ? NotFound("No audiobook file with that id") : _library.ToFile(file, _records.Get(file.Id, file.Path));
    }

    /// <summary>
    /// Saves or clears a file's ASIN.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="request">The ASIN and region.</param>
    /// <returns>The file's updated row.</returns>
    [HttpPut("Files/{itemId}/Asin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AdminFileDto> SetAsin([FromRoute] Guid itemId, [FromBody] AsinRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        if (string.IsNullOrWhiteSpace(request.Asin))
        {
            _records.SetAsin(file.Id, file.Path, null, null);
        }
        else
        {
            if (!AudnexusClient.TryNormalizeAsin(request.Asin, out var asin))
            {
                return BadRequest(AudnexusClient.BadAsinMessage(asin));
            }

            if (!AudnexusClient.TryNormalizeRegion(request.Region ?? Config.AudibleRegion, out var region))
            {
                return BadRequest(AudnexusClient.BadRegionMessage(region));
            }

            _records.SetAsin(file.Id, file.Path, asin, region);
        }

        return _library.ToFile(file, _records.Get(file.Id, file.Path));
    }

    /// <summary>
    /// Gets Audible's chapters for a file's saved ASIN, and how well its runtime matches the file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The chapters and the runtime check.</returns>
    [HttpGet("Files/{itemId}/Audible")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AudibleMatchDto>> GetAudible([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        var record = _records.Get(file.Id, file.Path);
        if (record?.Asin is not { } asin)
        {
            return BadRequest("Save an ASIN for this file first");
        }

        var region = record.Region ?? Config.AudibleRegion;
        var chapters = await _audnexus.GetChaptersAsync(asin, region, cancellationToken).ConfigureAwait(false);
        if (!chapters.IsSuccess)
        {
            return Failure(chapters);
        }

        // The title is only there to reassure, so a failed book lookup doesn't stop the chapters
        var book = await _audnexus.GetBookAsync(asin, region, cancellationToken).ConfigureAwait(false);
        var match = AudibleMatch.Compare(chapters.Value, DurationSec(file));
        return new AudibleMatchDto(
            book.Value?.Title,
            asin,
            region,
            match.AudibleSec,
            match.FileSec,
            match.DifferenceSec,
            match.Matches,
            chapters.IsOld,
            match.DroppedPastEnd,
            match.Chapters);
    }

    /// <summary>
    /// Starts a silence scan of a file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">How quiet counts as silence in dB, the plugin setting when left out.</param>
    /// <returns>The scan's state.</returns>
    [HttpPost("Files/{itemId}/Silences")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SilencePreviewDto> StartSilences([FromRoute] Guid itemId, [FromQuery] int? noise)
    {
        var noiseDb = noise ?? Config.SilenceNoiseDb;
        if (!IsValidNoise(noiseDb))
        {
            return BadRequest("Silence loudness must be between -80 and -10 dB");
        }

        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        var status = _silences.Start(new SilenceTarget(file.Id, file.Path, DurationSec(file), noiseDb));
        return Accepted(ToPreview(status, noiseDb, Config.SilenceMinSeconds));
    }

    /// <summary>
    /// Gets a silence scan's state, with suggested chapters once it's done.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">The loudness scanned at, the plugin setting when left out.</param>
    /// <param name="minSeconds">The shortest silence to count as a break, the plugin setting when left out.</param>
    /// <returns>The scan's state, None when it hasn't been scanned at this loudness.</returns>
    [HttpGet("Files/{itemId}/Silences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SilencePreviewDto> GetSilences([FromRoute] Guid itemId, [FromQuery] int? noise, [FromQuery] double? minSeconds)
    {
        var noiseDb = noise ?? Config.SilenceNoiseDb;
        var min = minSeconds ?? Config.SilenceMinSeconds;
        if (!IsValidNoise(noiseDb) || min < 1 || min > 30)
        {
            return BadRequest("Silence loudness must be between -80 and -10 dB, and the shortest silence between 1 and 30 s");
        }

        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        return ToPreview(_silences.Get(new SilenceTarget(file.Id, file.Path, DurationSec(file), noiseDb)), noiseDb, min);
    }

    /// <summary>
    /// Stops a silence scan that is waiting or running.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">The loudness being scanned at, the plugin setting when left out.</param>
    /// <returns>No content once stopped.</returns>
    [HttpDelete("Files/{itemId}/Silences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult CancelSilences([FromRoute] Guid itemId, [FromQuery] int? noise)
        => _silences.Cancel(itemId, noise ?? Config.SilenceNoiseDb) ? NoContent() : NotFound("No scan waiting or running");

    /// <summary>
    /// Starts writing the previewed chapters into a file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="request">The chapters and where they came from.</param>
    /// <returns>The write's state, it carries on in the background.</returns>
    [HttpPost("Files/{itemId}/Apply")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ApplyStatusDto> Apply([FromRoute] Guid itemId, [FromBody] ApplyRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        if (!M4bChapterWriter.CanWrite(file.Path))
        {
            return BadRequest("Only .m4b and .m4a files can get chapters");
        }

        if (request.Source is null || !_sources.Contains(request.Source))
        {
            return BadRequest("Source must be Audible or Silence");
        }

        // Checked here so a bad list is a 400 straight away, the writer checks again against the real file
        var problem = ChapterFile.Validate(request.Chapters, DurationSec(file));
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        var status = _writes.Start(new ChapterWriteTarget(file.Id, file.Path, request.Chapters!, request.Source));
        return Accepted(ToApply(status, file.Path));
    }

    /// <summary>
    /// Gets the last chapter write's state for a file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>The state, None when nothing was written since Jellyfin started.</returns>
    [HttpGet("Files/{itemId}/Apply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ApplyStatusDto> GetApply([FromRoute] Guid itemId)
    {
        var file = _library.FindFile(itemId);
        return file is null ? NotFound("No audiobook file with that id") : ToApply(_writes.Get(file.Id), file.Path);
    }

    /// <summary>
    /// Puts a file's original back from its .bak.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>No content once restored.</returns>
    [HttpPost("Files/{itemId}/Restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult Restore([FromRoute] Guid itemId)
    {
        var file = _library.FindFile(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        if (!System.IO.File.Exists(M4bChapterWriter.BackupPath(file.Path)))
        {
            return NotFound("This file has no .bak to restore");
        }

        var problem = _writes.Restore(file.Id, file.Path);
        return problem is null ? NoContent() : Conflict(problem);
    }

    private static double DurationSec(AudioBook file) => (file.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond;

    private static bool IsValidNoise(int noiseDb) => noiseDb is >= -80 and <= -10;

    private static SilencePreviewDto ToPreview(SilenceJobStatus? status, int noiseDb, double minSeconds)
    {
        if (status is null)
        {
            return new SilencePreviewDto("None", 0, null, noiseDb, minSeconds, null);
        }

        var chapters = status.Scan is { } scan
            ? BreakPicker.Pick(scan.Silences, minSeconds, scan.DurationSec).Select(c => new ChapterEntry(c.Title, c.StartSec)).ToList()
            : null;
        return new SilencePreviewDto(status.State.ToString(), status.Percent, status.Error, noiseDb, minSeconds, chapters);
    }

    private static ApplyStatusDto ToApply(ChapterWriteStatus? status, string path) => new(
        status?.State.ToString() ?? "None",
        status?.Stage?.ToString(),
        status?.Error,
        status?.FinishedAt,
        System.IO.File.Exists(M4bChapterWriter.BackupPath(path)));

    private ActionResult Failure<T>(AudnexusResult<T> result)
        where T : class
    {
        switch (result.Error)
        {
            case AudnexusError.InvalidAsin:
            case AudnexusError.InvalidRegion:
                return BadRequest(result.Message);
            case AudnexusError.NotFound:
                return NotFound(result.Message);
            case AudnexusError.RateLimited:
                return StatusCode(StatusCodes.Status429TooManyRequests, result.Message);
            default:
                // 502 says the service we depend on failed, not the plugin
                return StatusCode(StatusCodes.Status502BadGateway, result.Message);
        }
    }
}
