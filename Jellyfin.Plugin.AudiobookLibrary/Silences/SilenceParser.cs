using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AudiobookLibrary.Silences;

/// <summary>
/// We read ffmpeg's silencedetect lines, which come as a silence_start line followed by a silence_end line with the duration.
/// </summary>
public static partial class SilenceParser
{
    /// <summary>
    /// Pairs up the start and end lines into silences.
    /// </summary>
    /// <param name="lines">ffmpeg's stderr, other lines are skipped.</param>
    /// <returns>The silences in the order ffmpeg reported them.</returns>
    public static IReadOnlyList<Silence> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var silences = new List<Silence>();
        double? start = null;
        foreach (var line in lines)
        {
            var startMatch = StartPattern().Match(line);
            if (startMatch.Success)
            {
                // The filter can report a start slightly before 0 when a file opens quiet
                start = Math.Max(0, double.Parse(startMatch.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var endMatch = EndPattern().Match(line);
            if (endMatch.Success && start is { } s)
            {
                silences.Add(new Silence(
                    s,
                    double.Parse(endMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(endMatch.Groups[2].Value, CultureInfo.InvariantCulture)));
                start = null;
            }
        }

        return silences;
    }

    [GeneratedRegex(@"silence_start: (-?[\d.]+)")]
    private static partial Regex StartPattern();

    [GeneratedRegex(@"silence_end: ([\d.]+) \| silence_duration: ([\d.]+)")]
    private static partial Regex EndPattern();
}
