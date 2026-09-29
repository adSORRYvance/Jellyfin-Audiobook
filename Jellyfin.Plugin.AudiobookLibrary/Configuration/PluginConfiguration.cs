using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AudiobookLibrary.Configuration;

/// <summary>
/// Settings saved by the dashboard page. Old config files pick up the defaults for any field added later.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Audible store to look books up in. The same book has a different ASIN in each store.
    /// </summary>
    public string AudibleRegion { get; set; } = "us";
}
