using System;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models;
using Jellyfin.Plugin.AudiobookLibrary.Silences;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// A temporary admin-only window onto silence scans, so they can be checked on a real server before the admin page exists.
/// This whole file is removed before TM-225 ships, along with AudibleController.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AudiobookLibrary/Silences")]
public class SilenceController : ControllerBase
{
    private readonly SilenceJobs _jobs;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SilenceController"/> class.
    /// </summary>
    /// <param name="jobs">Runs and tracks the scans.</param>
    /// <param name="libraryManager">Finds the file behind an item.</param>
    public SilenceController(SilenceJobs jobs, ILibraryManager libraryManager)
    {
        _jobs = jobs;
        _libraryManager = libraryManager;
    }

    private static int ConfiguredNoise => Plugin.Instance?.Configuration.SilenceNoiseDb ?? -30;

    private static double ConfiguredMinSeconds => Plugin.Instance?.Configuration.SilenceMinSeconds ?? 3.0;

    /// <summary>
    /// Starts scanning an audiobook file for silences.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">How quiet counts as silence, in dB, the plugin setting when left out.</param>
    /// <returns>The scan's state, it carries on in the background.</returns>
    [HttpPost("{itemId}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SilenceScanDto> Start([FromRoute] Guid itemId, [FromQuery] int? noise)
    {
        var noiseDb = noise ?? ConfiguredNoise;
        if (!IsValidNoise(noiseDb))
        {
            return BadRequest("noise must be between -80 and -10 dB");
        }

        var target = Target(itemId, noiseDb);
        if (target is null)
        {
            return NotFound("No audiobook file with that id");
        }

        return Accepted(ToDto(_jobs.Start(target), noiseDb, ConfiguredMinSeconds));
    }

    /// <summary>
    /// Gets a scan's state, with suggested chapters once it's done.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">The loudness it was scanned at, the plugin setting when left out.</param>
    /// <param name="minSeconds">The shortest silence to count as a break, the plugin setting when left out.</param>
    /// <returns>The scan's state.</returns>
    [HttpGet("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SilenceScanDto> Get([FromRoute] Guid itemId, [FromQuery] int? noise, [FromQuery] double? minSeconds)
    {
        var noiseDb = noise ?? ConfiguredNoise;
        var min = minSeconds ?? ConfiguredMinSeconds;
        if (!IsValidNoise(noiseDb) || min < 1 || min > 30)
        {
            return BadRequest("noise must be between -80 and -10 dB and minSeconds between 1 and 30");
        }

        var target = Target(itemId, noiseDb);
        if (target is null)
        {
            return NotFound("No audiobook file with that id");
        }

        var status = _jobs.Get(target);
        return status is null
            ? NotFound("Not scanned at this loudness yet, POST to start a scan")
            : ToDto(status, noiseDb, min);
    }

    /// <summary>
    /// Stops a scan that is waiting or running.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="noise">The loudness it's being scanned at, the plugin setting when left out.</param>
    /// <returns>No content once stopped.</returns>
    [HttpDelete("{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Cancel([FromRoute] Guid itemId, [FromQuery] int? noise)
        => _jobs.Cancel(itemId, noise ?? ConfiguredNoise) ? NoContent() : NotFound("No scan waiting or running");

    private static bool IsValidNoise(int noiseDb) => noiseDb is >= -80 and <= -10;

    private static SilenceScanDto ToDto(SilenceJobStatus status, int noiseDb, double minSeconds)
    {
        var scan = status.Scan;
        return new SilenceScanDto(
            status.State.ToString(),
            status.Percent,
            status.Error,
            noiseDb,
            scan is null ? null : minSeconds,
            scan?.ScannedAt,
            scan?.Silences.Count,
            scan is null ? null : BreakPicker.Pick(scan.Silences, minSeconds, scan.DurationSec));
    }

    private SilenceTarget? Target(Guid itemId, int noiseDb)
    {
        // Admin only, so there's no per-user library check to make here
        if (_libraryManager.GetItemById(itemId) is not AudioBook file || string.IsNullOrEmpty(file.Path))
        {
            return null;
        }

        return new SilenceTarget(file.Id, file.Path, (file.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond, noiseDb);
    }
}
