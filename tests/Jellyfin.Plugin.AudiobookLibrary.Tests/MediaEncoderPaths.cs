using System.Reflection;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests;

// IMediaEncoder is large and our code only reads where ffmpeg and ffprobe are, so everything else throws
public class MediaEncoderPaths : DispatchProxy
{
    private string _ffmpeg = string.Empty;
    private string _ffprobe = string.Empty;

    public static IMediaEncoder Create(string ffmpeg, string ffprobe)
    {
        var encoder = Create<IMediaEncoder, MediaEncoderPaths>();
        var paths = (MediaEncoderPaths)(object)encoder;
        paths._ffmpeg = ffmpeg;
        paths._ffprobe = ffprobe;
        return encoder;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_EncoderPath" => _ffmpeg,
        "get_ProbePath" => _ffprobe,
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}
