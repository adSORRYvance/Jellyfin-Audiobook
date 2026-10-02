using System;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// A snapshot of a chapter write for whoever is polling it.
/// </summary>
/// <param name="State">Where the write is.</param>
/// <param name="Stage">Which step is running, while it runs.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="FinishedAt">When it finished or failed.</param>
public sealed record ChapterWriteStatus(ChapterWriteState State, ChapterWriteStage? Stage, string? Error, DateTimeOffset? FinishedAt);
