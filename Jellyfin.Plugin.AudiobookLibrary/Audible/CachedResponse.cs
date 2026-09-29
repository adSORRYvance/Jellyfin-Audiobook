using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// One saved Audnexus answer, kept as the raw text so fields we don't read yet are already there when a later feature needs them.
/// </summary>
/// <param name="FetchedAt">When Audnexus sent it.</param>
/// <param name="Status">The HTTP status, 200 or 404.</param>
/// <param name="Body">The response body as Audnexus sent it.</param>
public sealed record CachedResponse(DateTimeOffset FetchedAt, int Status, string Body);
