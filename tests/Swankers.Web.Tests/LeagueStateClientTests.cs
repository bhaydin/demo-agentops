using Swankers.Web.League;
using Swankers.Web.Tests.Support;

namespace Swankers.Web.Tests;

public sealed class LeagueStateClientTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task State_and_decisions_go_through_the_demo_api_with_the_admin_key()
    {
        await using var host = new WebTestHost();
        var client = host.Resolve<LeagueStateClient>();

        var state = await client.GetStateAsync(CT);
        var approved = await client.ResolveAsync("c-1", approve: true, CT);
        var denied = await client.ResolveAsync("c-2", approve: false, CT);

        Assert.Equal("Anchorage Falling", state.Owner?.Name);
        Assert.Equal(4, state.Week);
        Assert.Equal("Out", state.Owner!.Roster.Single(p => p.Id == "14823").RosterStatus);
        Assert.Contains(state.PendingTrades, t => t.Note.Contains("SYSTEM:"));
        Assert.True(state.Gate?.OwnerGateEnabled);
        Assert.Equal(WebTestHost.AdminKey, host.Api.LastAdminKey);

        Assert.NotNull(approved);
        Assert.NotNull(denied);
        Assert.Equal((true, "executed"), (approved.Approved, approved.Status));
        Assert.Equal((false, "denied"), (denied.Approved, denied.Status));
        Assert.Contains(host.Api.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/confirmations/c-1" && r.Body!.Contains("\"approve\":true"));
    }

    [Fact]
    public async Task A_decision_on_a_confirmation_that_is_gone_returns_null()
    {
        await using var host = new WebTestHost();
        host.Api.OnResolve = (_, _) => null;

        var outcome = await host.Resolve<LeagueStateClient>().ResolveAsync("stale", approve: true, CT);

        Assert.Null(outcome);
    }
}
