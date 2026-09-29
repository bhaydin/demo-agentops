using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Swankers.Web.League;

namespace Swankers.Web.Tests.Support;

/// <summary>Stands in for Swankers.Mcp's demo REST API: serves a state and records decisions.</summary>
public sealed class FakeLeagueApi : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public LeagueState State { get; set; } = Fixtures.State();

    public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

    public string? LastAdminKey { get; private set; }

    /// <summary>What POST /api/confirmations/{id} answers; null means 404 (no longer pending).</summary>
    public Func<string, bool, ApprovalOutcome?> OnResolve { get; set; } =
        (_, approve) => new ApprovalOutcome(approve, approve ? "executed" : "denied", null, null);

    /// <summary>When set, the raw response (or exception) for POST /api/confirmations/{id}: timeouts, 500s, garbage.</summary>
    public Func<string, bool, HttpResponseMessage>? OnResolveResponse { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method, path, body));
        LastAdminKey = request.Headers.TryGetValues("X-Demo-Admin-Key", out var keys) ? keys.First() : null;

        if (request.Method == HttpMethod.Get && path == "/api/state")
        {
            return Ok(State);
        }

        if (request.Method == HttpMethod.Post && path.StartsWith("/api/confirmations/", StringComparison.Ordinal))
        {
            var id = path["/api/confirmations/".Length..];
            var approve = JsonDocument.Parse(body ?? "{}").RootElement.GetProperty("approve").GetBoolean();
            if (OnResolveResponse is not null)
            {
                return OnResolveResponse(id, approve);
            }

            var outcome = OnResolve(id, approve);
            return outcome is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(outcome);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok<T>(T value)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: Json) };
}
