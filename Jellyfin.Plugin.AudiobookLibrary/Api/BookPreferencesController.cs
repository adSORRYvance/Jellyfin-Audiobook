using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models;
using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Jellyfin.Plugin.AudiobookLibrary.Preferences;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// Reads and saves the calling user's settings for a book, the playback speed and their place in the whole book.
/// The user always comes from the login, never the URL, so nobody can ask for someone else's settings.
/// </summary>
[ApiController]
[Authorize]
[Route("AudiobookLibrary/Books")]
public class BookPreferencesController : ControllerBase
{
    private readonly BookChapterService _books;
    private readonly SpeedStore _speeds;
    private readonly PositionStore _positions;
    private readonly IAuthorizationContext _authContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BookPreferencesController"/> class.
    /// </summary>
    /// <param name="books">Works out which book a file belongs to.</param>
    /// <param name="speeds">Where speeds are saved.</param>
    /// <param name="positions">Where places in books are saved.</param>
    /// <param name="authContext">Tells us which user is calling.</param>
    public BookPreferencesController(BookChapterService books, SpeedStore speeds, PositionStore positions, IAuthorizationContext authContext)
    {
        _books = books;
        _speeds = speeds;
        _positions = positions;
        _authContext = authContext;
    }

    /// <summary>
    /// Gets the calling user's settings for the book that an audiobook file belongs to.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <returns>The settings, with defaults for anything never saved.</returns>
    [HttpGet("{itemId}/Preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookPreferencesDto>> GetPreferences([FromRoute] Guid itemId)
    {
        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

        // Same rule as the chapters endpoint, a book the user can't see is a 404
        var book = _books.GetBookKey(itemId, auth.User);
        if (book is null)
        {
            return NotFound();
        }

        // An API key has no user, so it only ever sees the defaults
        var speed = auth.User is null ? SpeedStore.DefaultSpeed : _speeds.Get(auth.User.Id, book.Id);
        return new BookPreferencesDto(speed);
    }

    /// <summary>
    /// Saves the calling user's settings for the book that an audiobook file belongs to.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <param name="preferences">The new settings.</param>
    /// <returns>No content once saved.</returns>
    [HttpPut("{itemId}/Preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetPreferences([FromRoute] Guid itemId, [FromBody] BookPreferencesDto preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return BadRequest("An API key has no user to save settings for");
        }

        // Refused rather than clamped, so a player bug shows up instead of quietly saving the wrong speed
        if (!SpeedStore.TryNormalize(preferences.Speed, out var speed))
        {
            return BadRequest($"Speed must be between {SpeedStore.MinSpeed} and {SpeedStore.MaxSpeed}");
        }

        var book = _books.GetBookKey(itemId, auth.User);
        if (book is null)
        {
            return NotFound();
        }

        _speeds.Set(auth.User.Id, book.Id, speed, book.Title);
        return NoContent();
    }

    /// <summary>
    /// Gets the calling user's place in the book that an audiobook file belongs to.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <returns>The place, or 0 with no update time when it was never saved.</returns>
    [HttpGet("{itemId}/Position")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookPositionDto>> GetPosition([FromRoute] Guid itemId)
    {
        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

        var book = _books.GetBookKey(itemId, auth.User);
        if (book is null)
        {
            return NotFound();
        }

        var saved = auth.User is null ? null : _positions.Get(auth.User.Id, book.Id);
        return saved is null ? new BookPositionDto(0) : new BookPositionDto(saved.PositionSec, saved.UpdatedAt);
    }

    /// <summary>
    /// Saves the calling user's place in the book that an audiobook file belongs to.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <param name="position">The new place, its update time is ignored.</param>
    /// <returns>The place as saved.</returns>
    [HttpPut("{itemId}/Position")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookPositionDto>> SetPosition([FromRoute] Guid itemId, [FromBody] BookPositionDto position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (auth.User is null)
        {
            return BadRequest("An API key has no user to save a position for");
        }

        if (!PositionStore.TryNormalize(position.PositionSec, out var positionSec))
        {
            return BadRequest("Position must be a number of seconds, 0 or more");
        }

        var book = _books.GetBookKey(itemId, auth.User);
        if (book is null)
        {
            return NotFound();
        }

        var saved = _positions.Set(auth.User.Id, book.Id, positionSec, book.Title);
        return new BookPositionDto(saved.PositionSec, saved.UpdatedAt);
    }
}
