namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// A snapshot of a scan for whoever is polling it.
/// </summary>
/// <param name="State">Where the scan is.</param>
/// <param name="Percent">How much of the file ffmpeg has read.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="Scan">The result, once done.</param>
public sealed record SilenceJobStatus(SilenceJobState State, double Percent, string? Error, SilenceScan? Scan);
