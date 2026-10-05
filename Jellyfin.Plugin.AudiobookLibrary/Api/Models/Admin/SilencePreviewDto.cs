using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// A silence scan's state, and the suggested chapters once it's done.
/// </summary>
/// <param name="State">None, Queued, Running, Done, Failed or Cancelled.</param>
/// <param name="Percent">How much of the file ffmpeg has read.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="NoiseDb">The loudness that counts as silence.</param>
/// <param name="MinSeconds">The shortest silence counted as a break.</param>
/// <param name="Chapters">The suggested chapters, only set when done.</param>
public sealed record SilencePreviewDto(string State, double Percent, string? Error, int NoiseDb, double MinSeconds, IReadOnlyList<ChapterEntry>? Chapters);
