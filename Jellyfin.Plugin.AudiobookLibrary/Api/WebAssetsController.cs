using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// Serves the player script that index.html points at. It's anonymous because a script tag can't send auth headers and loads before login.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("AudiobookLibrary/web")]
public class WebAssetsController : ControllerBase
{
    /// <summary>
    /// Gets the jellyfin-web player plugin.
    /// </summary>
    /// <returns>The script, or 404 if it's missing from the DLL.</returns>
    [HttpGet("player.js")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetPlayerScript()
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.AudiobookLibrary.Web.player.js");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, "application/javascript");
    }
}
