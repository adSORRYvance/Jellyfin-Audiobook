using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// A finished scan of one file at one loudness, saved so picking chapters never needs ffmpeg again.
/// The file's size and modified time let us notice when the file changed and the scan no longer fits it.
/// </summary>
/// <param name="ItemId">The AudioBook item that was scanned.</param>
/// <param name="Path">The file that was scanned.</param>
/// <param name="FileSize">The file's size at scan time.</param>
/// <param name="FileModifiedUtc">The file's modified time at scan time.</param>
/// <param name="NoiseDb">Anything quieter than this counted as silence.</param>
/// <param name="DurationSec">The file's length.</param>
/// <param name="ScannedAt">When the scan finished.</param>
/// <param name="Silences">Every silence of at least a second.</param>
public sealed record SilenceScan(
    Guid ItemId,
    string Path,
    long FileSize,
    DateTime FileModifiedUtc,
    int NoiseDb,
    double DurationSec,
    DateTimeOffset ScannedAt,
    IReadOnlyList<Silence> Silences);
