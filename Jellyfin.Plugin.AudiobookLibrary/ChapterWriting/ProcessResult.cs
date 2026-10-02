using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.ChapterWriting;

/// <summary>
/// What a finished ffmpeg or ffprobe run printed.
/// </summary>
/// <param name="ExitCode">0 when it worked.</param>
/// <param name="Output">Lines from stdout.</param>
/// <param name="Errors">Lines from stderr.</param>
internal sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Errors);
