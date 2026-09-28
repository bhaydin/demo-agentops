using System.Net;
using Swankers.League.Mfl;
using Swankers.League.Models;
using Swankers.League.Tests.Support;

namespace Swankers.League.Tests;

public class MflExportClientTests
{
    [Fact]
    public async Task Requests_are_spaced_by_at_least_the_configured_gap()
    {
        var spacing = TimeSpan.FromMilliseconds(200);
        var (client, handler, _) = MflTest.Create(options: MflTest.Options(spacing));
        var ct = TestContext.Current.CancellationToken;

        await client.GetPlayersAsync(ct);
        await client.GetInjuriesAsync(1, ct);

        Assert.Equal(2, handler.Calls.Count);
        var gap = handler.Calls[1].TimestampUtc - handler.Calls[0].TimestampUtc;
        // Generous tolerance below 200ms for timer slop; the point is "not immediate".
        Assert.True(gap >= TimeSpan.FromMilliseconds(120), $"requests only {gap.TotalMilliseconds}ms apart");
    }

    [Fact]
    public async Task Responses_are_cached()
    {
        var (client, handler, _) = MflTest.Create();
        var ct = TestContext.Current.CancellationToken;

        await client.GetStandingsAsync(ct);
        await client.GetStandingsAsync(ct);

        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Registered_user_agent_is_sent_on_every_request()
    {
        var (client, handler, _) = MflTest.Create();

        await client.GetPlayersAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("SwankersTest/1.0", call.UserAgent);
    }

    [Fact]
    public async Task Throttled_response_falls_back_to_snapshot_without_retrying()
    {
        var fallback = new FakeSnapshotReader
        {
            Players = [new Player("1234", "Synthetic, Sam", "RB", "FA")],
        };
        var (client, handler, _) = MflTest.Create(
            responder: _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            fallback: fallback);

        var players = await client.GetPlayersAsync(TestContext.Current.CancellationToken);

        Assert.Single(handler.Calls); // MFL guidance: a failed request is not retried.
        Assert.Equal("1234", Assert.Single(players).Id);
    }

    [Fact]
    public async Task Error_envelope_with_http_200_is_a_failure_not_empty_data()
    {
        // Codex Phase 1 review #4: MFL signals bad key / unknown league as 200 + {"error": ...}.
        var fallback = new FakeSnapshotReader
        {
            Standings = [new Standing("0001", 1, 0, 0, 100m, 90m)],
        };
        var (client, handler, _) = MflTest.Create(
            responder: _ => RecordingHandler.Json("""{ "error": "Invalid API key" }"""),
            fallback: fallback);
        var ct = TestContext.Current.CancellationToken;

        var first = await client.GetStandingsAsync(ct);
        var second = await client.GetStandingsAsync(ct);

        Assert.Single(first);                     // served from the snapshot fallback
        Assert.Single(second);
        Assert.Equal(2, handler.Calls.Count);     // the error was not cached as data
    }

    [Fact]
    public async Task Error_envelope_without_fallback_throws_without_echoing_mfl_text()
    {
        var (client, _, logger) = MflTest.Create(
            responder: _ => RecordingHandler.Json("""{ "error": "Invalid API key TEST-KEY-do-not-log" }"""));

        var ex = await Assert.ThrowsAsync<MflResponseException>(
            () => client.GetPlayersAsync(TestContext.Current.CancellationToken));

        Assert.Equal("players", ex.RequestType);
        Assert.DoesNotContain("Invalid API key", ex.Message);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(MflTest.ApiKey, entry));
    }

    [Fact]
    public async Task Injuries_report_their_actual_source()
    {
        // Codex Phase 2 review #4: live, cache, and snapshot fallback must be distinguishable.
        const string live = """{ "injuries": { "week": "4", "injury": [ { "id": "1", "status": "Out", "details": "Knee" } ] } }""";
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(live));
        var ct = TestContext.Current.CancellationToken;

        var first = await client.GetInjuriesWithSourceAsync(4, ct);
        var second = await client.GetInjuriesWithSourceAsync(4, ct);

        Assert.Equal(DataSource.Live, first.Source);
        Assert.Equal(DataSource.Cache, second.Source);
        Assert.Equal(first.AsOf, second.AsOf);

        var manifest = new Snapshots.SnapshotManifest("2026-09-27", new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), 4, "synthetic");
        var (offline, _, _) = MflTest.Create(
            responder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            fallback: new FakeSnapshotReader { Injuries = [new Injury("2", "IR", "Ankle", null)], Manifest = manifest });

        var fallback = await offline.GetInjuriesWithSourceAsync(4, ct);

        Assert.Equal(DataSource.Snapshot, fallback.Source);
        Assert.Equal("2026-09-27", fallback.SnapshotId);
        Assert.Equal(manifest.CapturedAtUtc, fallback.AsOf);
        Assert.Equal("2", Assert.Single(fallback.Value).PlayerId);
    }

    [Fact]
    public async Task Failure_without_fallback_throws()
    {
        var (client, _, _) = MflTest.Create(
            responder: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetPlayersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Api_key_and_urls_never_reach_logs()
    {
        var (client, _, logger) = MflTest.Create(
            responder: _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            fallback: new FakeSnapshotReader());
        var ct = TestContext.Current.CancellationToken;

        await client.GetStandingsAsync(ct); // league request: URL carries the API key
        await client.GetPlayersAsync(ct);

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(MflTest.ApiKey, entry);
            Assert.DoesNotContain("APIKEY", entry);
            Assert.DoesNotContain("myfantasyleague.com/", entry);
        });
    }

    [Fact]
    public async Task Franchise_names_come_from_the_map_never_from_mfl()
    {
        // Synthetic league payload shaped like the real one, including PII-style fields.
        const string leagueJson = """
            {
              "league": {
                "franchises": {
                  "count": "2",
                  "franchise": [
                    { "id": "0001", "name": "Real Owner Team", "owner_name": "Pat Example",
                      "email": "pat@example.com", "cell": "555-0100" },
                    { "id": "0002", "name": "Other Team", "owner_name": "Sam Example",
                      "email": "sam@example.com" }
                  ]
                }
              }
            }
            """;
        var names = new FranchiseNameMap(new Dictionary<string, string> { ["0001"] = "The Swank" });
        var (client, _, _) = MflTest.Create(
            responder: _ => RecordingHandler.Json(leagueJson),
            names: names);

        var franchises = await client.GetFranchisesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, franchises.Count);
        Assert.Equal("The Swank", franchises.Single(f => f.Id == "0001").Name);
        Assert.Equal("Franchise 0002", franchises.Single(f => f.Id == "0002").Name);
        Assert.DoesNotContain(franchises, f =>
            f.Name.Contains("Example") || f.Name.Contains("Owner") || f.Name.Contains("@"));
    }
}
