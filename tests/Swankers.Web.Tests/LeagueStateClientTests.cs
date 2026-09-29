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

    // Codex Phase 6 P2: a timeout or an unreadable response escaped the dialog and ended the
    // circuit. DecideAsync never throws for those, and reads the league's record before
    // reporting a failure, because the request may have executed.

    [Fact]
    public async Task A_timed_out_decision_is_reconciled_from_the_league_record()
    {
        await using var host = new WebTestHost();
        var pending = Fixtures.State(pendingApproval: true).PendingConfirmations[0];
        host.Api.OnResolveResponse = (_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.");
        host.Api.State = Fixtures.State() with { RecentConfirmations = [new ResolvedApproval(pending, true, "executed", Fixtures.At, null)] };

        var result = await host.Resolve<LeagueStateClient>().DecideAsync("c-1", approve: true, CT);

        Assert.NotNull(result.Outcome);
        Assert.True(result.Reconciled);
        Assert.Equal("executed", result.Outcome.Status);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("text/html", "<html>gateway timeout</html>")]
    [InlineData("application/json", "{not json")]
    public async Task An_unreadable_response_with_the_action_still_pending_reports_the_failure_without_throwing(string mediaType, string body)
    {
        await using var host = new WebTestHost();
        host.Api.OnResolveResponse = (_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };
        host.Api.State = Fixtures.State(pendingApproval: true);

        var result = await host.Resolve<LeagueStateClient>().DecideAsync("c-1", approve: false, CT);

        Assert.Null(result.Outcome);
        Assert.False(result.Reconciled);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task A_server_error_after_the_action_executed_is_reconciled_too()
    {
        await using var host = new WebTestHost();
        var pending = Fixtures.State(pendingApproval: true).PendingConfirmations[0];
        host.Api.OnResolveResponse = (_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        host.Api.State = Fixtures.State() with { RecentConfirmations = [new ResolvedApproval(pending, false, "denied", Fixtures.At, null)] };

        var result = await host.Resolve<LeagueStateClient>().DecideAsync("c-1", approve: false, CT);

        Assert.True(result.Reconciled);
        Assert.Equal("denied", result.Outcome?.Status);
    }

    [Fact]
    public async Task A_confirmation_that_is_gone_resolves_to_its_record()
    {
        await using var host = new WebTestHost();
        var pending = Fixtures.State(pendingApproval: true).PendingConfirmations[0];
        host.Api.OnResolve = (_, _) => null;
        host.Api.State = Fixtures.State() with { RecentConfirmations = [new ResolvedApproval(pending, true, "executed", Fixtures.At, null)] };

        var result = await host.Resolve<LeagueStateClient>().DecideAsync("c-1", approve: true, CT);

        Assert.True(result.Reconciled);
        Assert.Equal("executed", result.Outcome?.Status);
    }
}
