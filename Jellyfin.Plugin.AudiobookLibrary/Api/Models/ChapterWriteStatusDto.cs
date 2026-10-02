using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// A chapter write's state, and whether the file has a backup to restore.
/// </summary>
/// <param name="State">Queued, Running, Done or Failed, or None when nothing was written since Jellyfin started.</param>
/// <param name="Stage">Which step is running: Preparing, Writing, Checking, Replacing or Refreshing.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="FinishedAt">When it finished or failed.</param>
/// <param name="HasBackup">Whether the original is kept as a .bak next to the file.</param>
public sealed record ChapterWriteStatusDto(string State, string? Stage, string? Error, DateTimeOffset? FinishedAt, bool HasBackup);
