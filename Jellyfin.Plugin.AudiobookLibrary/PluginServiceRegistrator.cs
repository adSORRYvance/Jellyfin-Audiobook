using System.IO;
using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Jellyfin.Plugin.AudiobookLibrary.Preferences;
using Jellyfin.Plugin.AudiobookLibrary.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary;

/// <summary>
/// Jellyfin calls this while it builds the web host, so anything we add here lands in the same pipeline as jellyfin-web itself.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddTransient<IStartupFilter, WebInjectionStartupFilter>();
        serviceCollection.AddSingleton<BookChapterService>();

        // A singleton so every request shares the one lock around the speed files
        // Under Jellyfin's data folder rather than ours, since a plugin update deletes the old version's folder
        serviceCollection.AddSingleton(services => new SpeedStore(
            Path.Combine(services.GetRequiredService<IApplicationPaths>().DataPath, "audiobook-library", "speeds"),
            services.GetRequiredService<ILogger<SpeedStore>>()));
    }
}
