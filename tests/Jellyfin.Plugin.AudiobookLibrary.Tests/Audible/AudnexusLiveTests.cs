using Jellyfin.Plugin.AudiobookLibrary.Audible;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Jellyfin.Plugin.AudiobookLibrary.Tests.Audible.AudnexusFixtures;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;

// Talks to the real api.audnex.us, so it only runs when asked
// dotnet run --project tests/Jellyfin.Plugin.AudiobookLibrary.Tests -c Release -- -explicit only
// It catches Audnexus changing its answers, which the fixture-based tests can't
public sealed class AudnexusLiveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "abl-audnexus-live-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact(Explicit = true)]
    public async Task RealAudnexus_ReturnsTheLostMetal()
    {
        var client = new AudnexusClient(
            new RealFactory(),
            new AudnexusCache(_folder, NullLogger<AudnexusCache>.Instance),
            TimeProvider.System,
            NullLogger<AudnexusClient>.Instance);
        var token = TestContext.Current.CancellationToken;

        var book = await client.GetBookAsync(LostMetalAsin, "us", token);
        var chapters = await client.GetChaptersAsync(LostMetalAsin, "us", token);
        var author = await client.GetAuthorAsync(SandersonAsin, "us", token);

        Assert.Equal("The Lost Metal", book.Value?.Title);
        Assert.Equal("The Mistborn Saga", book.Value?.SeriesPrimary?.Name);
        Assert.Equal(94, chapters.Value?.Chapters?.Count);
        Assert.Equal("Brandon Sanderson", author.Value?.Name);
    }

    private sealed class RealFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri("https://api.audnex.us/"), Timeout = TimeSpan.FromSeconds(15) };
    }
}
