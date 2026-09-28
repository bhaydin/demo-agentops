using ModelContextProtocol;
using Swankers.Mcp.Gate;

namespace Swankers.Mcp.Tests;

public class ConfirmationGateTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, object?> NoArgs = [];

    private static PendingConfirmationResult Queue(ConfirmationGate gate, Func<CancellationToken, Task<object>> execute, long? generation = null)
        => gate.Create("drop_player", "Drop X", "owner:0001", "0001", NoArgs, execute, generation ?? gate.Generation);

    [Fact]
    public async Task Approve_executes_the_parked_action_exactly_once()
    {
        var gate = new ConfirmationGate();
        var executions = 0;
        var pending = Queue(gate, _ => { executions++; return Task.FromResult<object>("dropped"); });

        Assert.Equal("pending_confirmation", pending.Status);
        Assert.Single(gate.Pending);

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.NotNull(outcome);
        Assert.True(outcome.Approved);
        Assert.Equal("executed", outcome.Status);
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
        var pending = Queue(gate, _ => { executed = true; return Task.FromResult<object>("dropped"); });

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: false, CT);

        Assert.Equal("denied", outcome!.Status);
        Assert.False(executed);
        Assert.Single(gate.Recent, r => !r.Approved);
    }

    [Fact]
    public async Task Execution_failure_is_reported_not_thrown()
    {
        var gate = new ConfirmationGate();
        var pending = Queue(gate, _ => throw new InvalidOperationException("no longer on roster"));

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.Equal("failed", outcome!.Status);
        Assert.Equal("no longer on roster", outcome.Error);
        var record = Assert.Single(gate.Recent);
        Assert.Equal("failed", record.Status);
        Assert.Empty(gate.Pending);
        Assert.Null(await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT)); // terminal: no retry
    }

    [Fact]
    public async Task Unexpected_failures_also_end_in_a_terminal_record()
    {
        var gate = new ConfirmationGate();
        var pending = Queue(gate, _ => throw new IOException("disk full"));

        var outcome = await gate.ResolveAsync(pending.ConfirmationId, approve: true, CT);

        Assert.Equal("failed", outcome!.Status);
        Assert.Contains("IOException", outcome.Error);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public async Task Unknown_id_is_null_and_reset_cancels_pending_work()
    {
        var gate = new ConfirmationGate();
        Queue(gate, _ => Task.FromResult<object>(1));
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
        var pending = Queue(gate, async _ =>
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

    [Fact]
    public async Task Action_prepared_before_a_reset_is_refused_at_creation()
    {
        // Codex Phase 3 review #2: a confirmation prepared against the old league must not be queued.
        var gate = new ConfirmationGate();
        var generation = gate.Generation; // the tool snapshots this before preparing
        await gate.ResetAsync(_ => Task.CompletedTask, CT);

        var ex = Assert.Throws<McpException>(() => Queue(gate, _ => Task.FromResult<object>(1), generation));

        Assert.Contains("reset", ex.Message);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public async Task Action_queued_while_a_reset_is_running_is_canceled_when_it_finishes()
    {
        // The tool prepared before the reset began (same generation), and reached the gate
        // while the league was being reseeded; the finished reset must sweep it away.
        var gate = new ConfirmationGate();
        var resetting = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var executed = false;

        var reset = gate.ResetAsync(async _ => { resetting.SetResult(); await release.Task; }, CT);
        await resetting.Task;
        var queued = Queue(gate, _ => { executed = true; return Task.FromResult<object>(1); }, generation: 0);
        Assert.Single(gate.Pending);

        release.SetResult();
        Assert.Equal(1, await reset);

        Assert.Empty(gate.Pending);
        Assert.Contains(gate.Recent, r => r.Status == "canceled" && r.Confirmation.Id == queued.ConfirmationId);
        Assert.Null(await gate.ResolveAsync(queued.ConfirmationId, approve: true, CT));
        Assert.False(executed);
    }
}
