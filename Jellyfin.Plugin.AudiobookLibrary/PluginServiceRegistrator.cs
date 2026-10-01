using System;
using System.IO;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Jellyfin.Plugin.AudiobookLibrary.Chapters;
using Jellyfin.Plugin.AudiobookLibrary.Preferences;
using Jellyfin.Plugin.AudiobookLibrary.Silences;
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
        serviceCollection.AddSingleton(services => new SpeedStore(
            DataFolder(services, "speeds"),
            services.GetRequiredService<ILogger<SpeedStore>>()));

        // The User-Agent tells the Audnexus maintainer who's calling if we ever cause trouble
        serviceCollection.AddHttpClient(AudnexusClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.audnex.us/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"JellyfinAudiobookLibrary/{typeof(Plugin).Assembly.GetName().Version} (+https://github.com/adSORRYvance/Jellyfin-Audiobook)");
        });
        serviceCollection.AddSingleton(services => new AudnexusCache(
            DataFolder(services, "audnexus"),
            services.GetRequiredService<ILogger<AudnexusCache>>()));

        // A singleton so a rate limit seen by one request holds back all the others
        serviceCollection.AddSingleton(services => new AudnexusClient(
            services.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
            services.GetRequiredService<AudnexusCache>(),
            TimeProvider.System,
            services.GetRequiredService<ILogger<AudnexusClient>>()));

        serviceCollection.AddSingleton<ISilenceScanner, FfmpegSilenceScanner>();
        serviceCollection.AddSingleton(services => new SilenceCache(
            DataFolder(services, "silences"),
            services.GetRequiredService<ILogger<SilenceCache>>()));

        // A singleton so one ffmpeg at a time holds across every request, and shutdown stops what's running
        serviceCollection.AddSingleton(services => new SilenceJobs(
            services.GetRequiredService<ISilenceScanner>(),
            services.GetRequiredService<SilenceCache>(),
            TimeProvider.System,
            services.GetRequiredService<ILogger<SilenceJobs>>()));
    }

    // Under Jellyfin's data folder rather than ours, since a plugin update deletes the old version's folder
    private static string DataFolder(IServiceProvider services, string name)
        => Path.Combine(services.GetRequiredService<IApplicationPaths>().DataPath, "audiobook-library", name);
}
