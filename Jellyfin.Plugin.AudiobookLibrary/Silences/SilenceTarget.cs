using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// What to scan: one file at one loudness.
/// </summary>
/// <param name="ItemId">The file's AudioBook item.</param>
/// <param name="Path">The file on disk.</param>
/// <param name="DurationSec">The file's length, for the progress percent.</param>
/// <param name="NoiseDb">Anything quieter than this counts as silence.</param>
public sealed record SilenceTarget(Guid ItemId, string Path, double DurationSec, int NoiseDb);
