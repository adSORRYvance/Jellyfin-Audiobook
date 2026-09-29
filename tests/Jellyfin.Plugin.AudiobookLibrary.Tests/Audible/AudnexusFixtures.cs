namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;

// Shortened copies of what api.audnex.us sent for The Lost Metal and Brandon Sanderson on 2026-09-29
internal static class AudnexusFixtures
{
    public const string LostMetalAsin = "B09N446YK7";
    public const string SandersonAsin = "B001IGFHW6";

    public const string Book = """
        {"asin":"B09N446YK7","authors":[{"asin":"B001IGFHW6","name":"Brandon Sanderson"}],"copyright":2022,
        "description":"The Mistborn Saga continues.","formatType":"unabridged","image":"https://m.media-amazon.com/images/I/91seqIlXmCL.jpg",
        "isAdult":false,"language":"english","narrators":[{"name":"Michael Kramer"}],"publisherName":"Macmillan Audio",
        "rating":"4.9","region":"us","releaseDate":"2022-11-15T00:00:00.000Z","runtimeLengthMin":1126,
        "seriesPrimary":{"asin":"B006K1P698","name":"The Mistborn Saga","position":"7"},
        "seriesSecondary":{"asin":"B0DMXTJ8WH","name":"The Cosmere"},
        "subtitle":"A Mistborn Novel","summary":"<p>The Mistborn Saga continues.</p>","title":"The Lost Metal"}
        """;

    public const string Chapters = """
        {"asin":"B09N446YK7","brandIntroDurationMs":3924,"brandOutroDurationMs":4945,
        "chapters":[{"lengthMs":37543,"startOffsetMs":0,"startOffsetSec":0,"title":"Opening Credits"},
        {"lengthMs":567243,"startOffsetMs":37543,"startOffsetSec":37,"title":"Acknowledgments"},
        {"lengthMs":1257221,"startOffsetMs":604786,"startOffsetSec":604,"title":"Prologue"}],
        "isAccurate":true,"region":"us","runtimeLengthMs":67619694,"runtimeLengthSec":67619}
        """;

    public const string Author = """
        {"asin":"B001IGFHW6","description":"I'm Brandon Sanderson, and I write stories of the fantastic.",
        "genres":[{"asin":"18580606011","name":"Science Fiction & Fantasy","type":"genre"}],
        "image":"https://images-na.ssl-images-amazon.com/images/S/amzn-author-media-prod/example.jpg",
        "name":"Brandon Sanderson","region":"us","similar":[{"asin":"B07FZX5GSD","name":"Christopher Ruocchio"}]}
        """;

    public const string NotInRegion = """
        {"error":{"code":"REGION_UNAVAILABLE","message":"Item not available in region 'uk' for ASIN: B09N446YK7","details":{"asin":"B09N446YK7","code":"REGION_UNAVAILABLE"}}}
        """;
}
