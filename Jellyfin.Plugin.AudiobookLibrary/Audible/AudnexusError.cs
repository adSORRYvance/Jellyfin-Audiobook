namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// The ways an Audnexus lookup can fail that callers are expected to handle.
/// </summary>
public enum AudnexusError
{
    /// <summary>
    /// The lookup worked.
    /// </summary>
    None,

    /// <summary>
    /// The ASIN isn't ten letters and digits, so we never sent it.
    /// </summary>
    InvalidAsin,

    /// <summary>
    /// The region isn't one of Audible's stores.
    /// </summary>
    InvalidRegion,

    /// <summary>
    /// Audnexus has nothing for this ASIN in this region.
    /// </summary>
    NotFound,

    /// <summary>
    /// Audnexus asked us to slow down.
    /// </summary>
    RateLimited,

    /// <summary>
    /// Audnexus is down, timed out or sent something we couldn't read.
    /// </summary>
    Unavailable
}
