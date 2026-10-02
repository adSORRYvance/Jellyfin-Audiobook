using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// The last chapter write's state for a file.
/// </summary>
/// <param name="State">None, Queued, Running, Done or Failed.</param>
/// <param name="Stage">Preparing, Writing, Checking, Replacing or Refreshing while it runs.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="FinishedAt">When it finished or failed.</param>
/// <param name="HasBackup">Whether the original is kept as a .bak.</param>
public sealed record ApplyStatusDto(string State, string? Stage, string? Error, DateTimeOffset? FinishedAt, bool HasBackup);
