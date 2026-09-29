using System.Net;

namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;

// Stands in for api.audnex.us, answering from a queue or a fixed reply and counting every request
internal sealed class FakeAudnexus : HttpMessageHandler, IHttpClientFactory
{
    public Func<HttpRequestMessage, HttpResponseMessage> Reply { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public List<string> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public HttpClient CreateClient(string name)
        => new(this, false) { BaseAddress = new Uri("https://api.audnex.us/") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        return Task.FromResult(Reply(request));
    }
}
