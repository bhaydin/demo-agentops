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
    public async Task Unknown_id_is_null_and_clear_empties_everything()
    {
        var gate = new ConfirmationGate();
        gate.Create("t", "s", "owner:0001", "0001", new Dictionary<string, object?>(), _ => Task.FromResult<object>(1));

        Assert.Null(await gate.ResolveAsync("nope", approve: true, CT));
        gate.Clear();
        Assert.Empty(gate.Pending);
        Assert.Empty(gate.Recent);
    }
}
