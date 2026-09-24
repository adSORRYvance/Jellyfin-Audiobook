using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.AudiobookLibrary.Web;

/// <summary>
/// Puts our middleware in front of Jellyfin's own pipeline, ahead of the static files that serve jellyfin-web.
/// </summary>
public class WebInjectionStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebInjectionMiddleware>();
            next(app);
        };
    }
}
