using System.Reflection;
using Jellyfin.Plugin.AudiobookLibrary.Silences;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Silences;

public class FfmpegSilenceScannerTests
{
    [Fact]
    public void Arguments_KeepPathWithSpacesAsOneArgument()
    {
        var args = FfmpegSilenceScanner.Arguments("/books/The Lost Metal꞉ A Mistborn Novel.m4b", -30).ToList();

        Assert.Contains("/books/The Lost Metal꞉ A Mistborn Novel.m4b", args);
        Assert.Contains("silencedetect=noise=-30dB:d=1", args);
        Assert.Equal("-vn", args[args.IndexOf("-i") + 2]);
    }

    // Runs the real ffmpeg on a generated file, so it only runs when asked and FFMPEG points at a binary
    // FFMPEG=/path/to/ffmpeg dotnet run --project tests/Jellyfin.Plugin.AudiobookLibrary.Tests -c Release -- -explicit only
    [Fact(Explicit = true)]
    public async Task RealFfmpeg_FindsTheGapInAGeneratedFile()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG");
        Assert.False(string.IsNullOrEmpty(ffmpeg), "set FFMPEG to an ffmpeg binary");
        var token = TestContext.Current.CancellationToken;
        var file = Path.Combine(Path.GetTempPath(), $"abl-{Guid.NewGuid():N}.m4a");

        // 44 s of tone with 4 s of silence starting at 20 s
        using (var make = System.Diagnostics.Process.Start(ffmpeg!, [
            "-hide_banner", "-loglevel", "error", "-f", "lavfi",
            "-i", "aevalsrc=if(between(t\\,20\\,24)\\,0\\,0.5*sin(2*PI*440*t)):s=44100:d=44",
            "-c:a", "aac", file]))
        {
            await make.WaitForExitAsync(token);
        }

        try
        {
            var encoder = DispatchProxy.Create<IMediaEncoder, EncoderPathOnly>();
            ((EncoderPathOnly)(object)encoder).Path = ffmpeg!;
            var scanner = new FfmpegSilenceScanner(encoder, NullLogger<FfmpegSilenceScanner>.Instance);
            var progress = new List<double>();

            var silences = await scanner.ScanAsync(file, -30, progress.Add, token);

            var gap = Assert.Single(silences);
            Assert.InRange(gap.StartSec, 19.8, 20.2);
            Assert.InRange(gap.DurationSec, 3.8, 4.2);
            Assert.NotEmpty(progress);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // IMediaEncoder is large and the scanner only reads EncoderPath, so everything else throws
    public class EncoderPathOnly : DispatchProxy
    {
        public string Path { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "get_EncoderPath" ? Path : throw new NotSupportedException(targetMethod?.Name);
    }
}
