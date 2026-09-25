using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudiobookLibrary.Api.Models;
using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// Gives the player a book's files and chapters on one timeline, in seconds because that's what the audio element uses.
/// </summary>
[ApiController]
[Authorize]
[Route("AudiobookLibrary/Books")]
public class BookChaptersController : ControllerBase
{
    private const double TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly BookChapterService _books;
    private readonly IAuthorizationContext _authContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BookChaptersController"/> class.
    /// </summary>
    /// <param name="books">Builds the book timeline.</param>
    /// <param name="authContext">Tells us which user is calling.</param>
    public BookChaptersController(BookChapterService books, IAuthorizationContext authContext)
    {
        _books = books;
        _authContext = authContext;
    }

    /// <summary>
    /// Gets the chapters of the book that an audiobook file belongs to.
    /// </summary>
    /// <param name="itemId">Any AudioBook item of the book.</param>
    /// <returns>The book's tracks and chapters, with an empty chapter list when the book has none.</returns>
    [HttpGet("{itemId}/Chapters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookChaptersDto>> GetChapters([FromRoute] Guid itemId)
    {
        var auth = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

        // A book the user can't see is a 404 too, so its titles don't leak through this endpoint
        var book = _books.GetBook(itemId, auth.User);
        if (book is null)
        {
            return NotFound();
        }

        return new BookChaptersDto(
            ToSeconds(book.DurationTicks),
            book.Tracks.Select(t => new BookTrackDto(t.ItemId, ToSeconds(t.StartTicks), ToSeconds(t.DurationTicks))).ToList(),
            book.Chapters.Select(c => new BookChapterDto(
                c.Title,
                ToSeconds(c.StartTicks),
                ToSeconds(c.EndTicks),
                c.TrackIndex,
                ToSeconds(c.TrackOffsetTicks))).ToList());
    }

    private static double ToSeconds(long ticks) => ticks / TicksPerSecond;
}
