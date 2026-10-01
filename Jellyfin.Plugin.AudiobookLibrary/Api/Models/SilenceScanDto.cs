using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AudiobookLibrary.Silences;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models;

/// <summary>
/// A silence scan's state, and the suggested chapters once it's done.
/// </summary>
/// <param name="State">Queued, Running, Done, Failed or Cancelled.</param>
/// <param name="Percent">How much of the file ffmpeg has read.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="NoiseDb">The loudness that counted as silence.</param>
/// <param name="MinSeconds">The shortest silence counted as a break, only set when done.</param>
/// <param name="ScannedAt">When the scan finished.</param>
/// <param name="SilenceCount">How many silences of a second or more the scan found.</param>
/// <param name="Chapters">The suggested chapters, only set when done.</param>
public sealed record SilenceScanDto(
    string State,
    double Percent,
    string? Error,
    int NoiseDb,
    double? MinSeconds,
    DateTimeOffset? ScannedAt,
    int? SilenceCount,
    IReadOnlyList<SilenceChapter>? Chapters);
