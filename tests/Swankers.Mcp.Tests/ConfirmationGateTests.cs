using Swankers.Mcp.Gate;

namespace Swankers.Mcp.Tests;

public class ConfirmationGateTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approve_executes_the_parked_action_exactly_once()
    {
        var gate = new ConfirmationGate();
        var executions = 0;
        var pending = gate.Create("drop_player", "Drop X", "owner:0001", "0001",
            new Dictionary<string, object?> { ["playerId"] = "1002" },
            _ => { executions++; return Task.FromResult<object>("dropped"); });

        Assert.Equal("pending_confirmation", pending.Status);
        Assert.Single(gate.Pending);

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.NotNull(outcome);
        Assert.True(outcome.Approved);
        Assert.Equal("dropped", outcome.Result);
        Assert.Equal(1, executions);
        Assert.Empty(gate.Pending);
        Assert.Null(await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT)); // gone
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Deny_never_executes()
    {
        var gate = new ConfirmationGate();
        var executed = false;
        var pending = gate.Create("drop_player", "Drop X", "owner:0001", "0001",
            new Dictionary<string, object?>(), _ => { executed = true; return Task.FromResult<object>("dropped"); });

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: false, CT);

        Assert.False(outcome!.Approved);
        Assert.False(executed);
        Assert.Single(gate.Recent, r => !r.Approved);
    }

    [Fact]
    public async Task Execution_failure_is_reported_not_thrown()
    {
        var gate = new ConfirmationGate();
        var pending = gate.Create("drop_player", "Drop X", "owner:0001", "0001",
            new Dictionary<string, object?>(), _ => throw new InvalidOperationException("no longer on roster"));

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.Equal("failed", outcome!.Status);
        Assert.Equal("no longer on roster", outcome.Error);
        var record = Assert.Single(gate.Recent);
        Assert.Equal("failed", record.Status);
        Assert.Equal("no longer on roster", record.Error);
        Assert.Empty(gate.Pending);
        Assert.Null(await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT)); // terminal: no retry
    }

    [Fact]
    public async Task Unexpected_failures_also_end_in_a_terminal_record()
    {
        // Codex Phase 2 review #3: a persistence failure must not make the decision disappear.
        var gate = new ConfirmationGate();
        var pending = gate.Create("drop_player", "Drop X", "owner:0001", "0001",
            new Dictionary<string, object?>(), _ => throw new IOException("disk full"));

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.Equal("failed", outcome!.Status);
        Assert.Contains("IOException", outcome.Error);
        Assert.Equal("failed", Assert.Single(gate.Recent).Status);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public async Task Outcomes_carry_a_status()
    {
        var gate = new ConfirmationGate();
        var ok = gate.Create("t", "s", "owner:0001", "0001", new Dictionary<string, object?>(), _ => Task.FromResult<object>(1));
        var no = gate.Create("t", "s", "owner:0001", "0001", new Dictionary<string, object?>(), _ => Task.FromResult<object>(1));

        Assert.Equal("executed", (await gate.ResolveAsync(ok.ConfirmationId, approve: true, CT))!.Status);
        Assert.Equal("denied", (await gate.ResolveAsync(no.ConfirmationId, approve: false, CT))!.Status);
    }

    [Fact]
    public async Task Unknown_id_is_null_and_reset_cancels_pending_work()
    {
        var gate = new ConfirmationGate();
        gate.Create("t", "s", "owner:0001", "0001", new Dictionary<string, object?>(), _ => Task.FromResult<object>(1));
        var leagueResets = 0;

        Assert.Null(await gate.ResolveAsync("nope", approve: true, CT));
        var canceled = await gate.ResetAsync(_ => { leagueResets++; return Task.CompletedTask; }, CT);

        Assert.Equal(1, canceled);
        Assert.Equal(1, leagueResets);
        Assert.Empty(gate.Pending);
        Assert.Equal("canceled", Assert.Single(gate.Recent).Status);
    }

    [Fact]
    public async Task Reset_waits_for_an_in_flight_approval_and_runs_the_league_reset_after_it()
    {
        // Codex Phase 2 review #2: reset must drain executing approvals, not race them.
        var gate = new ConfirmationGate();
        var order = new List<string>();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var pending = gate.Create("drop_player", "Drop X", "owner:0001", "0001", new Dictionary<string, object?>(),
            async _ =>
            {
                started.SetResult();
                await release.Task;
                order.Add("executed");
                return "dropped";
            });

        var approval = gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);
        await started.Task;
        var reset = gate.ResetAsync(_ => { order.Add("reset"); return Task.CompletedTask; }, CT);

        await Task.Delay(200, CT);
        Assert.False(reset.IsCompleted, "reset completed while an approval was still executing");

        release.SetResult();
        Assert.Equal("executed", (await approval)!.Status);
        Assert.Equal(0, await reset);
        Assert.Equal(["executed", "reset"], order);
        Assert.Null(await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT));
    }
}
