using System;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// We queue a metadata refresh so Jellyfin probes the rewritten file and saves its new chapters.
/// </summary>
public sealed class JellyfinItemRefresher : IItemRefresher
{
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinItemRefresher"/> class.
    /// </summary>
    /// <param name="providerManager">Runs Jellyfin's refreshes.</param>
    /// <param name="fileSystem">Needed for the refresh's directory listing.</param>
    public JellyfinItemRefresher(IProviderManager providerManager, IFileSystem fileSystem)
    {
        _providerManager = providerManager;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public void Refresh(Guid itemId)
    {
        // A full refresh re-probes the file, existing metadata and images stay unless the file says otherwise
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
            ReplaceAllMetadata = false,
            ReplaceAllImages = false
        };
        _providerManager.QueueRefresh(itemId, options, RefreshPriority.High);
    }
}
