namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// One quiet stretch ffmpeg found in a file.
/// </summary>
/// <param name="StartSec">Where the silence starts.</param>
/// <param name="EndSec">Where the sound comes back.</param>
/// <param name="DurationSec">How long it lasted.</param>
public sealed record Silence(double StartSec, double EndSec, double DurationSec);
