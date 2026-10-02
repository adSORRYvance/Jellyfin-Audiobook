using System;
using System.IO;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// A temporary admin-only window onto chapter writing, so it can be checked on a real server before the admin page exists.
/// This whole file is removed before TM-225 ships, along with AudibleController and SilenceController.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AudiobookLibrary/ChapterWrites")]
public class ChapterWriteController : ControllerBase
{
    private readonly ChapterWriteJobs _jobs;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterWriteController"/> class.
    /// </summary>
    /// <param name="jobs">Runs and tracks the writes.</param>
    /// <param name="libraryManager">Finds the file behind an item.</param>
    public ChapterWriteController(ChapterWriteJobs jobs, ILibraryManager libraryManager)
    {
        _jobs = jobs;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Starts writing chapters into an audiobook file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <param name="request">The chapters.</param>
    /// <returns>The write's state, it carries on in the background.</returns>
    [HttpPost("{itemId}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ChapterWriteStatusDto> Start([FromRoute] Guid itemId, [FromBody] ChapterWriteRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = File(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        if (!M4bChapterWriter.CanWrite(file.Path))
        {
            return BadRequest("Only .m4b and .m4a files can get chapters");
        }

        // Checked here too so a bad list is a 400 straight away, the writer checks again against the real file
        var problem = ChapterFile.Validate(request.Chapters, (file.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        var status = _jobs.Start(new ChapterWriteTarget(file.Id, file.Path, request.Chapters!));
        return Accepted(ToDto(status, file.Path));
    }

    /// <summary>
    /// Gets the last write's state for an audiobook file.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>The state.</returns>
    [HttpGet("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ChapterWriteStatusDto> Get([FromRoute] Guid itemId)
    {
        var file = File(itemId);
        return file is null ? NotFound("No audiobook file with that id") : ToDto(_jobs.Get(file.Id), file.Path);
    }

    /// <summary>
    /// Puts an audiobook file's original back from its .bak.
    /// </summary>
    /// <param name="itemId">The AudioBook item.</param>
    /// <returns>No content once restored.</returns>
    [HttpPost("{itemId}/Restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult Restore([FromRoute] Guid itemId)
    {
        var file = File(itemId);
        if (file is null)
        {
            return NotFound("No audiobook file with that id");
        }

        if (!System.IO.File.Exists(M4bChapterWriter.BackupPath(file.Path)))
        {
            return NotFound("This file has no .bak to restore");
        }

        var problem = _jobs.Restore(file.Id, file.Path);
        return problem is null ? NoContent() : Conflict(problem);
    }

    private static ChapterWriteStatusDto ToDto(ChapterWriteStatus? status, string path) => new(
        status?.State.ToString() ?? "None",
        status?.Stage?.ToString(),
        status?.Error,
        status?.FinishedAt,
        System.IO.File.Exists(M4bChapterWriter.BackupPath(path)));

    // Admin only, so there's no per-user library check to make here
    private AudioBook? File(Guid itemId)
        => _libraryManager.GetItemById(itemId) is AudioBook file && !string.IsNullOrEmpty(file.Path) ? file : null;
}
