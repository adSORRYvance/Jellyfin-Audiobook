using Jellyfin.Plugin.AudiobookLibrary.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
    }
}
