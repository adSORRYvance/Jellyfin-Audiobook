using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudiobookLibrary.Api;

/// <summary>
/// Serves the player files that index.html points at. It's anonymous because script and link tags can't send auth headers and load before login.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("AudiobookLibrary/web")]
public class WebAssetsController : ControllerBase
{
    // A fixed list rather than any embedded name, since anyone can call this route without logging in
    private static readonly FrozenDictionary<string, string> _assets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["player.js"] = "application/javascript; charset=utf-8",
        ["chapter-nav.js"] = "application/javascript; charset=utf-8",
        ["player.css"] = "text/css; charset=utf-8",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Gets one of the jellyfin-web player files.
    /// </summary>
    /// <param name="fileName">The file name, one of player.js, chapter-nav.js or player.css.</param>
    /// <returns>The file, or 404 for any other name.</returns>
    [HttpGet("{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetAsset([FromRoute] string fileName)
    {
        if (fileName is null || !_assets.TryGetValue(fileName, out var contentType))
        {
            return NotFound();
        }

        var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.AudiobookLibrary.Web." + fileName);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, contentType);
    }
}
