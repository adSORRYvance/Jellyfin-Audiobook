using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.AudiobookLibrary.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AudiobookLibrary;

/// <summary>
/// Entry point Jellyfin finds when it scans the plugins folder. We hand it our name, our fixed id and the dashboard page.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Server paths, used by the base class to find our config file.</param>
    /// <param name="xmlSerializer">Reads and writes our config XML.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Audiobook Library";

    /// <inheritdoc />
    /// <remarks>Never change this id. Saved settings and repository updates are matched on it.</remarks>
    public override Guid Id => Guid.Parse("4cecc660-432f-4714-8958-b5da8537e55d");

    /// <summary>
    /// Gets the loaded plugin, so services can read the current config.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
