using System.Net;
using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Jellyfin.Plugin.AudiobookLibrary.Tests.Audible.AudnexusFixtures;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;

public sealed class AudnexusClientTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-audnexus-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAudnexus _api = new();
    private readonly FakeClock _clock = new();

    public AudnexusClientTests()
    {
        // Answers like the real service, which only sells these in the US store
        _api.Reply = request => request.RequestUri!.Query != "?region=us"
            ? FakeAudnexus.Json(HttpStatusCode.NotFound, NotInRegion)
            : request.RequestUri.AbsolutePath switch
        {
            "/books/B09N446YK7" => FakeAudnexus.Json(HttpStatusCode.OK, Book),
            "/books/B09N446YK7/chapters" => FakeAudnexus.Json(HttpStatusCode.OK, AudnexusFixtures.Chapters),
            "/authors/B001IGFHW6" => FakeAudnexus.Json(HttpStatusCode.OK, Author),
            _ => FakeAudnexus.Json(HttpStatusCode.NotFound, NotInRegion)
        };
    }

    public void Dispose()
    {
        _api.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    // A new client over the same folder behaves like the server after a restart
    private AudnexusClient NewClient()
        => new(_api, new AudnexusCache(_folder, NullLogger<AudnexusCache>.Instance), _clock, NullLogger<AudnexusClient>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task KnownAsin_ReturnsBookChaptersAndAuthor()
    {
        var client = NewClient();

        var book = await client.GetBookAsync(LostMetalAsin, "us", Token);
        var chapters = await client.GetChaptersAsync(LostMetalAsin, "us", Token);
        var author = await client.GetAuthorAsync(SandersonAsin, "us", Token);

        Assert.True(book.IsSuccess);
        Assert.Equal("The Lost Metal", book.Value.Title);
        Assert.Equal("B001IGFHW6", book.Value.Authors![0].Asin);
        Assert.Equal("Michael Kramer", Assert.Single(book.Value.Narrators!).Name);
        Assert.Equal(new AudibleSeries("B006K1P698", "The Mistborn Saga", "7"), book.Value.SeriesPrimary);
        Assert.Null(book.Value.SeriesSecondary!.Position);

        Assert.True(chapters.IsSuccess);
        Assert.Equal(67619694, chapters.Value.RuntimeLengthMs);
        Assert.Equal(new AudibleChapter("Prologue", 604786, 1257221), chapters.Value.Chapters![2]);

        Assert.True(author.IsSuccess);
        Assert.Equal("Brandon Sanderson", author.Value.Name);
        Assert.StartsWith("I'm Brandon Sanderson", author.Value.Description, StringComparison.Ordinal);

        Assert.Equal(["/books/B09N446YK7?region=us", "/books/B09N446YK7/chapters?region=us", "/authors/B001IGFHW6?region=us"], _api.Requests);
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        await NewClient().GetBookAsync(LostMetalAsin, "us", Token);
        _clock.Now += TimeSpan.FromDays(29);

        var again = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.True(again.IsSuccess);
        Assert.False(again.IsOld);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AfterThirtyDays_FetchesAgain()
    {
        await NewClient().GetBookAsync(LostMetalAsin, "us", Token);
        _clock.Now += TimeSpan.FromDays(31);

        var again = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.Equal(_clock.Now, again.FetchedAt);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task Regions_AreCachedSeparately()
    {
        var client = NewClient();

        var us = await client.GetBookAsync(LostMetalAsin, "us", Token);
        var uk = await client.GetBookAsync(LostMetalAsin, "UK", Token);

        Assert.True(us.IsSuccess);
        Assert.Equal(AudnexusError.NotFound, uk.Error);
        Assert.Equal("/books/B09N446YK7?region=uk", _api.Requests[1]);
    }

    [Fact]
    public async Task NotFound_UsesAudnexusMessageAndIsCachedForADay()
    {
        var first = await NewClient().GetBookAsync(LostMetalAsin, "uk", Token);
        var second = await NewClient().GetBookAsync(LostMetalAsin, "uk", Token);
        _clock.Now += TimeSpan.FromDays(2);
        await NewClient().GetBookAsync(LostMetalAsin, "uk", Token);

        Assert.Equal(AudnexusError.NotFound, first.Error);
        Assert.Equal("Item not available in region 'uk' for ASIN: B09N446YK7", first.Message);
        Assert.Equal(AudnexusError.NotFound, second.Error);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task Down_WithOldCopy_ReturnsOldCopy()
    {
        await NewClient().GetBookAsync(LostMetalAsin, "us", Token);
        _clock.Now += TimeSpan.FromDays(40);
        _api.Reply = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsOld);
        Assert.Equal(_clock.Now - TimeSpan.FromDays(40), result.FetchedAt);
    }

    [Fact]
    public async Task Down_WithNothingCached_ReturnsUnavailable()
    {
        _api.Reply = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(AudnexusError.Unavailable, result.Error);
        Assert.Equal("Audnexus answered with HTTP 503", result.Message);
    }

    [Fact]
    public async Task Unreachable_ReturnsUnavailableInsteadOfThrowing()
    {
        _api.Reply = _ => throw new HttpRequestException("Name or service not known");

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.Equal(AudnexusError.Unavailable, result.Error);
    }

    [Fact]
    public async Task Timeout_ReturnsUnavailableInsteadOfThrowing()
    {
        // HttpClient reports its own timeout as a cancellation nobody asked for
        _api.Reply = _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.Equal(AudnexusError.Unavailable, result.Error);
    }

    [Fact]
    public async Task UnreadableBody_ReturnsUnavailableAndSavesNothing()
    {
        _api.Reply = _ => FakeAudnexus.Json(HttpStatusCode.OK, "<html>maintenance</html>");

        var first = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);
        await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.Equal(AudnexusError.Unavailable, first.Error);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task RateLimited_StopsAskingUntilTheResetTime()
    {
        _api.Reply = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("x-ratelimit-reset", "42");
            return response;
        };
        var client = NewClient();

        var first = await client.GetBookAsync(LostMetalAsin, "us", Token);
        var during = await client.GetAuthorAsync(SandersonAsin, "us", Token);
        _clock.Now += TimeSpan.FromSeconds(43);
        await client.GetAuthorAsync(SandersonAsin, "us", Token);

        Assert.Equal(AudnexusError.RateLimited, first.Error);
        Assert.Equal(_clock.Now - TimeSpan.FromSeconds(43) + TimeSpan.FromSeconds(42), first.RetryAt);
        Assert.Equal(AudnexusError.RateLimited, during.Error);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("../../../x")]
    [InlineData("B09N446YK7/chapters")]
    [InlineData("B09N446YK")]
    [InlineData("")]
    public async Task BadAsin_IsRefusedBeforeAnyRequest(string asin)
    {
        var result = await NewClient().GetBookAsync(asin, "us", Token);

        Assert.Equal(AudnexusError.InvalidAsin, result.Error);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task LowerCaseAsinWithSpaces_IsCleanedUp()
    {
        var result = await NewClient().GetBookAsync(" b09n446yk7 ", "us", Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("/books/B09N446YK7?region=us", Assert.Single(_api.Requests));
    }

    [Fact]
    public async Task BadRegion_IsRefusedBeforeAnyRequest()
    {
        var result = await NewClient().GetBookAsync(LostMetalAsin, "zz", Token);

        Assert.Equal(AudnexusError.InvalidRegion, result.Error);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task PositionWithDecimal_StaysText()
    {
        _api.Reply = _ => FakeAudnexus.Json(HttpStatusCode.OK, Book.Replace("\"position\":\"7\"", "\"position\":\"2.5\"", StringComparison.Ordinal));

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.Equal("2.5", result.Value!.SeriesPrimary!.Position);
    }

    [Fact]
    public async Task DamagedCacheFile_AsksAgain()
    {
        await NewClient().GetBookAsync(LostMetalAsin, "us", Token);
        await File.WriteAllTextAsync(Path.Combine(_folder, "us", "books", LostMetalAsin + ".json"), "{ not json", Token);

        var result = await NewClient().GetBookAsync(LostMetalAsin, "us", Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _api.Requests.Count);
    }
}
