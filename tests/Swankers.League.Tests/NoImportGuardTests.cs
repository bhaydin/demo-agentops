using System.Net;
using System.Reflection;
using Swankers.League.Mfl;
using Swankers.League.Tests.Support;

namespace Swankers.League.Tests;

/// <summary>
/// Enforces AGENTS.md hard rule 1: MflExportClient may only issue export requests.
/// Do not remove or weaken these tests.
/// </summary>
public class NoImportGuardTests
{
    [Fact]
    public async Task Every_reader_request_is_an_MFL_export()
    {
        var (client, handler, _) = MflTest.Create();
        var ct = TestContext.Current.CancellationToken;

        await client.GetFranchisesAsync(ct);
        await client.GetPlayersAsync(ct);
        await client.GetRostersAsync(ct);
        await client.GetRosterAsync("0001", ct);
        await client.GetInjuriesAsync(3, ct);
        await client.GetMatchupsAsync(3, ct);
        await client.GetProjectionsAsync(3, ct);
        await client.GetStandingsAsync(ct);
        await client.GetTransactionsAsync(25, ct);
        await client.GetPendingTradesAsync("0001", ct);

        Assert.NotEmpty(handler.Calls);
        Assert.All(handler.Calls, call =>
        {
            Assert.EndsWith(".myfantasyleague.com", call.Uri.Host);
            Assert.Contains("/export", call.Uri.AbsolutePath);
            Assert.DoesNotContain("import", call.Uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("JSON=1", call.Uri.Query);
        });
    }

    [Fact]
    public void Request_builder_cannot_express_an_import_url()
    {
        var builder = new MflRequestBuilder(MflTest.Options());
        var uriMethods = typeof(MflRequestBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(Uri))
            .ToList();

        Assert.NotEmpty(uriMethods);
        foreach (var method in uriMethods)
        {
            var args = method.GetParameters()
                .Select(p => (object?)(p.ParameterType == typeof(int?) ? 3 : p.ParameterType == typeof(int) ? 3 : null))
                .ToArray();
            var uri = (Uri)method.Invoke(builder, args)!;

            Assert.Contains("/export", uri.AbsolutePath);
            Assert.DoesNotContain("import", uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Export_only_handler_blocks_import_requests()
    {
        using var http = new HttpClient(new ExportOnlyHandler { InnerHandler = new RecordingHandler() });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(
            "https://www42.myfantasyleague.com/2026/import?TYPE=lineup",
            TestContext.Current.CancellationToken));

        Assert.Contains("import", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_only_handler_blocks_non_mfl_hosts()
    {
        using var http = new HttpClient(new ExportOnlyHandler { InnerHandler = new RecordingHandler() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(
            "https://example.com/2026/export?TYPE=players",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MflExportClient_does_not_implement_ILeagueWriter()
        => Assert.False(typeof(ILeagueWriter).IsAssignableFrom(typeof(MflExportClient)));
}
