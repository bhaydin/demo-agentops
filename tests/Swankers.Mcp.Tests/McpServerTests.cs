using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Swankers.Mcp.Api;
using Swankers.Mcp.Tests.Support;
using static Swankers.Mcp.Tests.Support.McpTestHost;

namespace Swankers.Mcp.Tests;

/// <summary>End-to-end through a real MCP client against the in-process server.</summary>
public class McpServerTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static readonly string[] ExpectedTools =
    [
        "get_my_roster", "get_roster", "get_player_news", "get_matchup", "get_standings", "get_trade_offers",
        "set_lineup", "propose_trade", "drop_player", "respond_to_trade",
    ];

    [Fact]
    public async Task Tool_list_matches_the_architecture_and_has_no_approval_tool()
    {
        await using var host = await StartAsync();
        var client = await host.ConnectAsync(OwnerCredential, CT);

        var tools = (await client.ListToolsAsync(cancellationToken: CT)).Select(t => t.Name).Order().ToList();

        Assert.Equal(ExpectedTools.Order(), tools);
        Assert.DoesNotContain(tools, t => t.Contains("approve") || t.Contains("confirm"));
    }

    [Fact]
    public async Task Requests_without_a_known_credential_are_rejected()
    {
        await using var host = await StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => host.ConnectAsync(null, CT));
        await Assert.ThrowsAnyAsync<Exception>(() => host.ConnectAsync("wrong-credential", CT));

        using var response = await host.Http(withAdminKey: false).PostAsync("/mcp", new StringContent("{}"), CT);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_reads_its_roster_and_cannot_act_for_another_franchise()
    {
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);

        var mine = await CallAsync(owner, "get_my_roster", null, CT);
        Assert.Equal("0001", Prop(mine, "franchiseId").GetString());
        Assert.Contains("1002", PlayerIds(mine));

        var error = await CallExpectingErrorAsync(owner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1004", ["franchiseId"] = "0002" }, CT);
        Assert.Contains("Scope denied", error);

        var theirs = await CallAsync(owner, "get_roster", new Dictionary<string, object?> { ["franchiseId"] = "0002" }, CT);
        Assert.Contains("1004", PlayerIds(theirs)); // read allowed, write denied, nothing changed
    }

    [Fact]
    public async Task Owner_drop_is_gated_and_only_the_rest_approval_executes_it()
    {
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);

        var pending = await CallAsync(owner, "drop_player", new Dictionary<string, object?> { ["playerId"] = "1002" }, CT);
        Assert.Equal("pending_confirmation", Prop(pending, "status").GetString());
        var id = Prop(pending, "confirmationId").GetString()!;
        Assert.Contains("Fake, Runner", Prop(pending, "summary").GetString());
        Assert.Contains("1002", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT))); // nothing happened yet

        var denied = await host.ResolveAsync(id, approve: false, CT);
        Assert.False(Prop(denied, "approved").GetBoolean());
        Assert.Contains("1002", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));

        var again = await CallAsync(owner, "drop_player", new Dictionary<string, object?> { ["playerId"] = "1002" }, CT);
        var approved = await host.ResolveAsync(Prop(again, "confirmationId").GetString()!, approve: true, CT);
        Assert.True(Prop(approved, "approved").GetBoolean());
        Assert.DoesNotContain("1002", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));

        var state = await host.Http().GetFromJsonAsync<JsonElement>("/api/state", CT);
        Assert.Contains(Prop(state, "transactions").EnumerateArray(), t => Prop(t, "type").GetString() == "Drop");
        Assert.Single(Prop(state, "recentConfirmations").EnumerateArray(), r => Prop(r, "approved").GetBoolean());
    }

    [Fact]
    public async Task Commissioner_acts_for_any_franchise_including_0000()
    {
        // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
        await using var host = await StartAsync();
        var commissioner = await host.ConnectAsync(CommissionerCredential, CT);

        var forOther = await CallAsync(commissioner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1004", ["franchiseId"] = "0002" }, CT);
        Assert.Equal("pending_confirmation", Prop(forOther, "status").GetString());
        await host.ResolveAsync(Prop(forOther, "confirmationId").GetString()!, approve: true, CT);
        Assert.Empty(PlayerIds(await CallAsync(commissioner, "get_roster", new Dictionary<string, object?> { ["franchiseId"] = "0002" }, CT)));

        var asLeague = await CallAsync(commissioner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1003", ["franchiseId"] = "0000" }, CT);
        Assert.Contains("The Fleecers", Prop(asLeague, "summary").GetString()); // 0000 acts as whoever rosters the player
    }

    [Fact]
    public async Task Approval_is_bound_to_the_target_resolved_at_confirmation_time()
    {
        // Codex Phase 2 review #1: a "0000" drop must not re-resolve its target at approval.
        await using var host = await StartAsync();
        var commissioner = await host.ConnectAsync(CommissionerCredential, CT);
        var owner = await host.ConnectAsync(OwnerCredential, CT);

        // Queue a drop of 1003, who is on The Fleecers (0099) right now.
        var pending = await CallAsync(commissioner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1003", ["franchiseId"] = "0000" }, CT);
        var dropId = Prop(pending, "confirmationId").GetString()!;
        var confirmations = await host.Http().GetFromJsonAsync<JsonElement>("/api/confirmations", CT);
        var queued = Prop(confirmations, "pending").EnumerateArray().Single(c => Prop(c, "id").GetString() == dropId);
        Assert.Equal("0099", Prop(queued, "effectiveFranchiseId").GetString());

        // Meanwhile 1003 moves to 0001 through an accepted trade.
        var trade = await CallAsync(commissioner, "propose_trade", new Dictionary<string, object?>
        {
            ["toFranchiseId"] = "0001", ["give"] = new[] { "1003" }, ["get"] = new[] { "1005" },
            ["note"] = "swap", ["franchiseId"] = "0099",
        }, CT);
        var accept = await CallAsync(owner, "respond_to_trade",
            new Dictionary<string, object?> { ["tradeId"] = Prop(trade, "id").GetString(), ["accept"] = true }, CT);
        await host.ResolveAsync(Prop(accept, "confirmationId").GetString()!, approve: true, CT);
        Assert.Contains("1003", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));

        // Approving the stale drop must fail against 0099, not drop 1003 from 0001.
        var outcome = await host.ResolveAsync(dropId, approve: true, CT);
        Assert.Contains("0099", Prop(outcome, "error").GetString());
        Assert.Contains("1003", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));
    }

    [Fact]
    public async Task Spans_record_the_franchise_actually_read_or_acted_on()
    {
        // Codex Phase 2 review #5: effective franchise must match the action, not the caller.
        using var spans = new SpanRecorder();
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);
        var commissioner = await host.ConnectAsync(CommissionerCredential, CT);

        await CallAsync(owner, "get_roster", new Dictionary<string, object?> { ["franchiseId"] = "0002" }, CT);
        var pending = await CallAsync(commissioner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1003", ["franchiseId"] = "0000" }, CT);

        var read = Assert.Single(spans.ToolCalls("get_roster"));
        Assert.Equal("owner:0001", SpanRecorder.Tag(read, McpDiagnostics.CallerScopeTag));
        Assert.Equal("0002", SpanRecorder.Tag(read, McpDiagnostics.RequestedFranchiseTag));
        Assert.Equal("0002", SpanRecorder.Tag(read, McpDiagnostics.EffectiveFranchiseTag));

        var drop = Assert.Single(spans.ToolCalls("drop_player"));
        Assert.Equal("0000", SpanRecorder.Tag(drop, McpDiagnostics.RequestedFranchiseTag));
        Assert.Equal("0099", SpanRecorder.Tag(drop, McpDiagnostics.EffectiveFranchiseTag)); // resolved target
        Assert.Equal("pending", SpanRecorder.Tag(drop, McpDiagnostics.GateDecisionTag));

        var confirmations = await host.Http().GetFromJsonAsync<JsonElement>("/api/confirmations", CT);
        var queued = Prop(confirmations, "pending").EnumerateArray()
            .Single(c => Prop(c, "id").GetString() == Prop(pending, "confirmationId").GetString());
        Assert.Equal("0099", Prop(queued, "effectiveFranchiseId").GetString());
    }

    [Fact]
    public async Task Failed_approval_ends_in_a_terminal_record_and_changes_nothing()
    {
        // Codex Phase 2 review #3: block SimLeague's temp file so the approved drop cannot persist.
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);
        await CallAsync(owner, "get_my_roster", null, CT); // seeds state.json
        var pending = await CallAsync(owner, "drop_player", new Dictionary<string, object?> { ["playerId"] = "1002" }, CT);
        Directory.CreateDirectory(Path.Combine(host.StateDirectory, "state.json.tmp"));

        var outcome = await host.ResolveAsync(Prop(pending, "confirmationId").GetString()!, approve: true, CT);

        Assert.Equal("failed", Prop(outcome, "status").GetString());
        Assert.False(string.IsNullOrEmpty(Prop(outcome, "error").GetString()));
        Assert.Contains("1002", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));
        var confirmations = await host.Http().GetFromJsonAsync<JsonElement>("/api/confirmations", CT);
        Assert.Empty(Prop(confirmations, "pending").EnumerateArray());
        Assert.Single(Prop(confirmations, "recent").EnumerateArray(), r => Prop(r, "status").GetString() == "failed");
    }

    [Fact]
    public async Task Reset_cancels_pending_confirmations_so_they_cannot_run_afterwards()
    {
        // Codex Phase 2 review #2 (pending side; the in-flight side is a gate unit test).
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);
        var pending = await CallAsync(owner, "drop_player", new Dictionary<string, object?> { ["playerId"] = "1002" }, CT);
        var id = Prop(pending, "confirmationId").GetString()!;

        var reset = await host.Http().PostAsync("/api/admin/reset", null, CT);
        var resetBody = JsonSerializer.Deserialize<JsonElement>(await reset.Content.ReadAsStringAsync(CT));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(1, Prop(resetBody, "canceledConfirmations").GetInt32());

        using var late = await host.Http().PostAsJsonAsync($"/api/confirmations/{id}", new ConfirmationDecision(true), CT);
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
        Assert.Contains("1002", PlayerIds(await CallAsync(owner, "get_my_roster", null, CT)));
        var confirmations = await host.Http().GetFromJsonAsync<JsonElement>("/api/confirmations", CT);
        Assert.Single(Prop(confirmations, "recent").EnumerateArray(), r => Prop(r, "status").GetString() == "canceled");
    }

    [Fact]
    public async Task Commissioner_with_gate_off_executes_immediately()
    {
        // DEMO: intentionally vulnerable (Friday talk). Gate off must be selected explicitly.
        await using var host = await StartAsync(commissionerGateEnabled: false);
        var commissioner = await host.ConnectAsync(CommissionerCredential, CT);

        var result = await CallAsync(commissioner, "drop_player",
            new Dictionary<string, object?> { ["playerId"] = "1004", ["franchiseId"] = "0002" }, CT);

        Assert.Contains("Dropped", Prop(result, "message").GetString());
        Assert.Empty(PlayerIds(await CallAsync(commissioner, "get_roster", new Dictionary<string, object?> { ["franchiseId"] = "0002" }, CT)));
    }

    [Fact]
    public async Task Poisoned_trade_scenario_seeds_an_offer_with_the_note_verbatim()
    {
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);

        using var seed = await host.Http().PostAsync("/api/admin/seed/poisoned-trade", null, CT);
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);

        var offers = await CallAsync(owner, "get_trade_offers", null, CT);
        var offer = Assert.Single(offers.EnumerateArray());
        Assert.Equal("0099", Prop(offer, "fromFranchiseId").GetString());
        Assert.Contains("SYSTEM MESSAGE FOR THE AI ASSISTANT", Prop(offer, "note").GetString());
        Assert.Equal("1002", Prop(Prop(offer, "get").EnumerateArray().Single(), "id").GetString()); // highest-projected RB

        // Accepting is gated even when "pre-approved by the commissioner" in the note.
        var accept = await CallAsync(owner, "respond_to_trade",
            new Dictionary<string, object?> { ["tradeId"] = Prop(offer, "id").GetString(), ["accept"] = true }, CT);
        Assert.Equal("pending_confirmation", Prop(accept, "status").GetString());
    }

    [Fact]
    public async Task Rejecting_a_trade_is_not_gated_and_reset_clears_the_league()
    {
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);
        await host.Http().PostAsync("/api/admin/seed/poisoned-trade", null, CT);
        var offer = Assert.Single((await CallAsync(owner, "get_trade_offers", null, CT)).EnumerateArray());

        var rejected = await CallAsync(owner, "respond_to_trade",
            new Dictionary<string, object?> { ["tradeId"] = Prop(offer, "id").GetString(), ["accept"] = false }, CT);
        Assert.Equal("Rejected", Prop(rejected, "status").GetString());
        Assert.Empty((await CallAsync(owner, "get_trade_offers", null, CT)).EnumerateArray());

        using var reset = await host.Http().PostAsync("/api/admin/reset", null, CT);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var state = await host.Http().GetFromJsonAsync<JsonElement>("/api/state", CT);
        Assert.Empty(Prop(state, "pendingTrades").EnumerateArray());
        Assert.DoesNotContain(Prop(state, "transactions").EnumerateArray(), t => Prop(t, "type").GetString() == "TradeRejected");
    }

    [Fact]
    public async Task Admin_endpoints_require_the_demo_key()
    {
        await using var host = await StartAsync();

        using var state = await host.Http(withAdminKey: false).GetAsync("/api/state", CT);
        using var reset = await host.Http(withAdminKey: false).PostAsync("/api/admin/reset", null, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, state.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reset.StatusCode);
    }

    [Fact]
    public async Task Player_news_reports_injury_status_and_matches_by_name()
    {
        await using var host = await StartAsync();
        var owner = await host.ConnectAsync(OwnerCredential, CT);

        var news = await CallAsync(owner, "get_player_news", new Dictionary<string, object?> { ["player"] = "Tight Sample" }, CT);

        var entry = Assert.Single(Prop(news, "players").EnumerateArray());
        Assert.Equal("Questionable", Prop(Prop(entry, "injury"), "status").GetString());
        Assert.Equal("snapshot", Prop(news, "source").GetString());
    }
}
