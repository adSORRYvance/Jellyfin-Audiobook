using Jellyfin.Plugin.AudiobookLibrary.Silences;
using Xunit;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Silences;

public class SilenceParserTests
{
    [Fact]
    public void Parse_PairsStartAndEndLines()
    {
        // Lines as jellyfin-ffmpeg 8.1 prints them, with other output mixed in
        string[] lines =
        [
            "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'book.m4b':",
            "[Parsed_silencedetect_0 @ 0x72bac0001c00] silence_start: 2.218073",
            "[Parsed_silencedetect_0 @ 0x72bac0001c00] silence_end: 3.769637 | silence_duration: 1.551565",
            "size=N/A time=00:00:05.00 bitrate=N/A speed= 400x",
            "[Parsed_silencedetect_0 @ 0x72bac0001c00] silence_start: 9.035351",
            "[Parsed_silencedetect_0 @ 0x72bac0001c00] silence_end: 12.698 | silence_duration: 3.662649"
        ];

        var silences = SilenceParser.Parse(lines);

        Assert.Equal([new Silence(2.218073, 3.769637, 1.551565), new Silence(9.035351, 12.698, 3.662649)], silences);
    }

    [Fact]
    public void Parse_NegativeStart_BecomesZero()
    {
        string[] lines =
        [
            "[Parsed_silencedetect_0 @ 0x0] silence_start: -0.00227",
            "[Parsed_silencedetect_0 @ 0x0] silence_end: 1.5 | silence_duration: 1.50227"
        ];

        Assert.Equal(0, Assert.Single(SilenceParser.Parse(lines)).StartSec);
    }

    [Fact]
    public void Parse_StartWithoutEnd_IsDropped()
    {
        // A file that ends in silence can stop before ffmpeg prints the end line
        string[] lines = ["[Parsed_silencedetect_0 @ 0x0] silence_start: 100.5"];

        Assert.Empty(SilenceParser.Parse(lines));
    }
}
