using System.Net;
using System.Text;

namespace Swankers.League.Tests.Support;

/// <summary>Terminal HTTP handler that records every request and serves canned responses.</summary>
public sealed class RecordingHandler(Func<Uri, HttpResponseMessage>? responder = null) : HttpMessageHandler
{
    public sealed record Call(Uri Uri, DateTimeOffset TimestampUtc, string? UserAgent);

    public List<Call> Calls { get; } = [];

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls.Add(new Call(
            request.RequestUri!,
            DateTimeOffset.UtcNow,
            request.Headers.TryGetValues("User-Agent", out var ua) ? string.Join(" ", ua) : null));

        return Task.FromResult(responder?.Invoke(request.RequestUri!) ?? Json("{}"));
    }
}
