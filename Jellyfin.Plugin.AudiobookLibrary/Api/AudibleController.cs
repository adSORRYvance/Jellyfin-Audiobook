using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// A temporary admin-only window onto the Audnexus client, so it can be checked on a real server before the admin page exists.
/// This whole file is removed before TM-225 ships, the admin page gets endpoints shaped for what it shows.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AudiobookLibrary/Audible")]
public class AudibleController : ControllerBase
{
    private readonly AudnexusClient _audnexus;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudibleController"/> class.
    /// </summary>
    /// <param name="audnexus">The Audnexus client.</param>
    public AudibleController(AudnexusClient audnexus)
    {
        _audnexus = audnexus;
    }

    private static string ConfiguredRegion => Plugin.Instance?.Configuration.AudibleRegion ?? "us";

    /// <summary>
    /// Gets a book's Audible details.
    /// </summary>
    /// <param name="asin">The book's ASIN.</param>
    /// <param name="region">The Audible store, the plugin setting when left out.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The book.</returns>
    [HttpGet("Books/{asin}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AudibleBook>> GetBook([FromRoute] string asin, [FromQuery] string? region, CancellationToken cancellationToken)
        => ToResult(await _audnexus.GetBookAsync(asin, region ?? ConfiguredRegion, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Gets a book's Audible chapters.
    /// </summary>
    /// <param name="asin">The book's ASIN.</param>
    /// <param name="region">The Audible store, the plugin setting when left out.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The chapters.</returns>
    [HttpGet("Books/{asin}/Chapters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AudibleChapters>> GetChapters([FromRoute] string asin, [FromQuery] string? region, CancellationToken cancellationToken)
        => ToResult(await _audnexus.GetChaptersAsync(asin, region ?? ConfiguredRegion, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Gets an author's Audible bio and photo.
    /// </summary>
    /// <param name="asin">The author's ASIN.</param>
    /// <param name="region">The Audible store, the plugin setting when left out.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The author.</returns>
    [HttpGet("Authors/{asin}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AudibleAuthor>> GetAuthor([FromRoute] string asin, [FromQuery] string? region, CancellationToken cancellationToken)
        => ToResult(await _audnexus.GetAuthorAsync(asin, region ?? ConfiguredRegion, cancellationToken).ConfigureAwait(false));

    private ActionResult<T> ToResult<T>(AudnexusResult<T> result)
        where T : class
    {
        if (result.IsSuccess)
        {
            // Lets a tester see a cache hit, the time stays put on the second call
            Response.Headers["X-Audnexus-Fetched-At"] = result.FetchedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Audnexus-Old"] = result.IsOld ? "true" : "false";
            return result.Value;
        }

        switch (result.Error)
        {
            case AudnexusError.InvalidAsin:
            case AudnexusError.InvalidRegion:
                return BadRequest(result.Message);
            case AudnexusError.NotFound:
                return NotFound(result.Message);
            case AudnexusError.RateLimited:
                if (result.RetryAt is { } retryAt)
                {
                    Response.Headers.RetryAfter = retryAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                }

                return StatusCode(StatusCodes.Status429TooManyRequests, result.Message);
            default:
                // 502 says the service we depend on failed, not the plugin
                return StatusCode(StatusCodes.Status502BadGateway, result.Message);
        }
    }
}
