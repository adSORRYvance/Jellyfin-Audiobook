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

    /// <summary>
    /// Gets or sets how quiet counts as silence when looking for chapter breaks.
    /// -40 dB missed every pause in a book with a little background hiss, -30 dB caught them.
    /// </summary>
    public int SilenceNoiseDb { get; set; } = -30;

    /// <summary>
    /// Gets or sets how long a silence has to be to count as a chapter break.
    /// 3 s found 87 of 93 breaks in The Lost Metal with 3 false ones, books with shorter gaps need less.
    /// </summary>
    public double SilenceMinSeconds { get; set; } = 3.0;
}
