using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// We ask Audnexus for Audible's book, chapter and author data, and save every answer so a book is looked up about once a month.
/// Audnexus is a free community service, so we cache hard, back off when it rate limits us, and fall back to old answers when it's down.
/// </summary>
public sealed partial class AudnexusClient
{
    /// <summary>
    /// The name of the HttpClient the service registrator sets up for us.
    /// </summary>
    public const string HttpClientName = "Audnexus";

    /// <summary>
    /// The Audible stores Audnexus accepts.
    /// </summary>
    public static readonly IReadOnlyList<string> Regions = ["au", "ca", "de", "es", "fr", "in", "it", "jp", "uk", "us"];

    // Book and chapter data almost never changes, and the admin page reloads the same books over and over
    private static readonly TimeSpan _freshFor = TimeSpan.FromDays(30);

    // Short, so a book Audible adds later shows up, but long enough that a typo doesn't call out on every click
    private static readonly TimeSpan _notFoundFreshFor = TimeSpan.FromDays(1);

    private static readonly TimeSpan _defaultCooldown = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AudnexusCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<AudnexusClient> _logger;

    // Ticks of the time a rate limit ends, shared by every request
    private long _cooldownUntilTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudnexusClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Hands out the Audnexus HttpClient.</param>
    /// <param name="cache">Where answers are saved.</param>
    /// <param name="time">The clock, so tests can move it.</param>
    /// <param name="logger">Logger.</param>
    public AudnexusClient(IHttpClientFactory httpClientFactory, AudnexusCache cache, TimeProvider time, ILogger<AudnexusClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets a book's details.
    /// </summary>
    /// <param name="asin">The book's ASIN in that region.</param>
    /// <param name="region">The Audible store.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The book, or why we don't have it.</returns>
    public Task<AudnexusResult<AudibleBook>> GetBookAsync(string asin, string region, CancellationToken cancellationToken)
        => GetAsync<AudibleBook>("books", asin, region, a => $"books/{a}", cancellationToken);

    /// <summary>
    /// Gets a book's chapters.
    /// </summary>
    /// <param name="asin">The book's ASIN in that region.</param>
    /// <param name="region">The Audible store.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The chapters, or why we don't have them.</returns>
    public Task<AudnexusResult<AudibleChapters>> GetChaptersAsync(string asin, string region, CancellationToken cancellationToken)
        => GetAsync<AudibleChapters>("chapters", asin, region, a => $"books/{a}/chapters", cancellationToken);

    /// <summary>
    /// Gets an author's bio and photo.
    /// </summary>
    /// <param name="asin">The author's ASIN.</param>
    /// <param name="region">The Audible store.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The author, or why we don't have them.</returns>
    public Task<AudnexusResult<AudibleAuthor>> GetAuthorAsync(string asin, string region, CancellationToken cancellationToken)
        => GetAsync<AudibleAuthor>("authors", asin, region, a => $"authors/{a}", cancellationToken);

    private async Task<AudnexusResult<T>> GetAsync<T>(string kind, string asin, string region, Func<string, string> path, CancellationToken cancellationToken)
        where T : class
    {
        // The ASIN ends up in a URL and a file name, so anything but ten letters and digits stops here
        // Upper case because people paste ASINs from all sorts of places and Audible's are always upper case
        asin = (asin ?? string.Empty).Trim().ToUpperInvariant();
        if (!AsinPattern().IsMatch(asin))
        {
            return new(AudnexusError.InvalidAsin, $"'{asin}' isn't an ASIN, those are 10 letters and digits like B09N446YK7");
        }

        region = (region ?? string.Empty).Trim().ToLowerInvariant();
        if (!Regions.Contains(region))
        {
            return new(AudnexusError.InvalidRegion, $"'{region}' isn't an Audible region, use one of {string.Join(", ", Regions)}");
        }

        var cached = _cache.Read(region, kind, asin);
        var now = _time.GetUtcNow();
        if (cached is not null && now - cached.FetchedAt < (cached.Status == 404 ? _notFoundFreshFor : _freshFor))
        {
            LogFromCache(kind, asin, region);
            return FromCached<T>(cached, asin, region, false);
        }

        // Asking again during a rate limit only makes it last longer
        var cooldownUntil = new DateTimeOffset(Interlocked.Read(ref _cooldownUntilTicks), TimeSpan.Zero);
        if (now < cooldownUntil)
        {
            return OldOr(cached, asin, region, new AudnexusResult<T>(AudnexusError.RateLimited, "Audnexus asked us to slow down, try again shortly", cooldownUntil));
        }

        var url = $"{path(asin)}?region={region}";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                {
                    var value = Parse<T>(body);
                    if (value is null)
                    {
                        LogUnexpectedBody(kind, asin, region);
                        return OldOr(cached, asin, region, new AudnexusResult<T>(AudnexusError.Unavailable, "Audnexus sent an answer we couldn't read"));
                    }

                    _cache.Write(region, kind, asin, new CachedResponse(now, 200, body));
                    LogFetched(kind, asin, region);
                    return new(value, now, false);
                }

                case HttpStatusCode.NotFound:
                    _cache.Write(region, kind, asin, new CachedResponse(now, 404, body));
                    return NotFound<T>(body, asin, region);

                case HttpStatusCode.TooManyRequests:
                {
                    var retryAt = now + RetryDelay(response);
                    Interlocked.Exchange(ref _cooldownUntilTicks, retryAt.UtcTicks);
                    LogRateLimited(retryAt);
                    return OldOr(cached, asin, region, new AudnexusResult<T>(AudnexusError.RateLimited, "Audnexus asked us to slow down, try again shortly", retryAt));
                }

                default:
                    LogBadStatus((int)response.StatusCode, kind, asin, region);
                    return OldOr(cached, asin, region, new AudnexusResult<T>(AudnexusError.Unavailable, $"Audnexus answered with HTTP {(int)response.StatusCode}"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A TaskCanceledException we didn't ask for is HttpClient's timeout
            LogRequestFailed(ex, kind, asin, region);
            return OldOr(cached, asin, region, new AudnexusResult<T>(AudnexusError.Unavailable, "Couldn't reach Audnexus, it may be down"));
        }
    }

    private static T? Parse<T>(string body)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AudnexusResult<T> FromCached<T>(CachedResponse cached, string asin, string region, bool isOld)
        where T : class
    {
        if (cached.Status == 404)
        {
            return NotFound<T>(cached.Body, asin, region);
        }

        var value = Parse<T>(cached.Body);
        return value is null
            ? new(AudnexusError.Unavailable, "The saved Audnexus answer couldn't be read")
            : new(value, cached.FetchedAt, isOld);
    }

    // An old answer beats an error when Audnexus is down, the admin page can say how old it is
    private static AudnexusResult<T> OldOr<T>(CachedResponse? cached, string asin, string region, AudnexusResult<T> failure)
        where T : class
    {
        if (cached?.Status == 200)
        {
            var old = FromCached<T>(cached, asin, region, true);
            if (old.IsSuccess)
            {
                return old;
            }
        }

        return failure;
    }

    private static AudnexusResult<T> NotFound<T>(string body, string asin, string region)
        where T : class
    {
        // Audnexus explains the 404, usually that the ASIN isn't sold in this region
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var text))
            {
                message = text.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return new(AudnexusError.NotFound, message ?? $"Audnexus has nothing for {asin} in region '{region}'");
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        // Audnexus sends the seconds left in its rate limit window rather than Retry-After
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return _defaultCooldown;
    }

    [GeneratedRegex("^[A-Z0-9]{10}$")]
    private static partial Regex AsinPattern();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Audnexus {Kind} {Asin} ({Region}) served from cache")]
    private partial void LogFromCache(string kind, string asin, string region);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Audnexus {Kind} {Asin} ({Region}) fetched from Audnexus")]
    private partial void LogFetched(string kind, string asin, string region);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audnexus rate limited us until {RetryAt}")]
    private partial void LogRateLimited(DateTimeOffset retryAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audnexus answered HTTP {Status} for {Kind} {Asin} ({Region})")]
    private partial void LogBadStatus(int status, string kind, string asin, string region);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audnexus sent a body we couldn't read for {Kind} {Asin} ({Region})")]
    private partial void LogUnexpectedBody(string kind, string asin, string region);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reach Audnexus for {Kind} {Asin} ({Region})")]
    private partial void LogRequestFailed(Exception ex, string kind, string asin, string region);
}
