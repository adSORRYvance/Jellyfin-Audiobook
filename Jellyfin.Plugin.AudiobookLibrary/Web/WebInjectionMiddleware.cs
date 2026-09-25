using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Web;

/// <summary>
/// We rewrite jellyfin-web's index.html and config.json on the way out so the browser loads our player as a window plugin.
/// Anything unexpected hands the request back to Jellyfin untouched, which leaves the stock player in charge.
/// </summary>
public partial class WebInjectionMiddleware
{
    /// <summary>
    /// The window property player.js defines, and the name we add to config.json's plugin list.
    /// </summary>
    public const string WindowPluginName = "AudiobookLibraryPlayer";

    private readonly RequestDelegate _next;
    private readonly IApplicationPaths _appPaths;
    private readonly IServerConfigurationManager _config;
    private readonly ILogger<WebInjectionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The rest of Jellyfin's pipeline.</param>
    /// <param name="appPaths">Server paths, used to find the jellyfin-web folder.</param>
    /// <param name="config">Server config, used for the base URL.</param>
    /// <param name="logger">Logger.</param>
    public WebInjectionMiddleware(
        RequestDelegate next,
        IApplicationPaths appPaths,
        IServerConfigurationManager config,
        ILogger<WebInjectionMiddleware> logger)
    {
        _next = next;
        _appPaths = appPaths;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Serves the rewritten file for the two paths we care about and passes everything else through.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var baseUrl = _config.GetNetworkConfiguration().BaseUrl.TrimEnd('/');
        var path = context.Request.Path.Value ?? string.Empty;

        string? body = null;
        string contentType = string.Empty;

        if (path.Equals(baseUrl + "/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals(baseUrl + "/web/index.html", StringComparison.OrdinalIgnoreCase))
        {
            body = await TryBuildIndexAsync(baseUrl).ConfigureAwait(false);
            contentType = "text/html; charset=utf-8";
        }
        else if (path.Equals(baseUrl + "/web/config.json", StringComparison.OrdinalIgnoreCase))
        {
            body = await TryBuildConfigAsync().ConfigureAwait(false);
            contentType = "application/json; charset=utf-8";
        }

        if (body is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = contentType;

        // Same as Jellyfin's own index.html, so a plugin update shows up on the next page load
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task<string?> TryBuildIndexAsync(string baseUrl)
    {
        try
        {
            var html = await File.ReadAllTextAsync(Path.Combine(_appPaths.WebPath, "index.html")).ConfigureAwait(false);
            var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headEnd < 0)
            {
                LogIndexUnexpected();
                return null;
            }

            // Plain scripts in head run in order and before the deferred jellyfin-web bundle calls loadPlugins
            // chapter-nav.js goes first because player.js reads it when the player is created
            var version = typeof(Plugin).Assembly.GetName().Version;
            var assets = $"{baseUrl}/AudiobookLibrary/web";
            var tags = $"<link rel=\"stylesheet\" href=\"{assets}/player.css?v={version}\">"
                + $"<script src=\"{assets}/chapter-nav.js?v={version}\"></script>"
                + $"<script src=\"{assets}/player.js?v={version}\"></script>";
            return html.Insert(headEnd, tags);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, "index.html");
            return null;
        }
    }

    private async Task<string?> TryBuildConfigAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(Path.Combine(_appPaths.WebPath, "config.json")).ConfigureAwait(false);
            if (JsonNode.Parse(json) is not JsonObject root || root["plugins"] is not JsonArray plugins)
            {
                LogConfigUnexpected();
                return null;
            }

            if (!plugins.Any(p => p?.GetValue<string>() == WindowPluginName))
            {
                plugins.Add(WindowPluginName);
            }

            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            LogReadFailed(ex, "config.json");
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "jellyfin-web index.html has no </head>, leaving it alone so the stock player is used")]
    private partial void LogIndexUnexpected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "jellyfin-web config.json has no plugins list, leaving it alone so the stock player is used")]
    private partial void LogConfigUnexpected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not rewrite jellyfin-web {File}, serving it unchanged")]
    private partial void LogReadFailed(Exception ex, string file);
}
