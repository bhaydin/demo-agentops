using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Sim;

/// <summary>
/// The simulated league: the ONLY implementation of ILeagueWriter in this codebase
/// (AGENTS.md hard rule 2). JSON-backed, seeded from a snapshot, and resettable to that
/// exact seeded state. All writes are serialized through one lock and persisted atomically.
/// </summary>
public sealed partial class SimLeague(
    SnapshotStore snapshots,
    string stateDirectory,
    TimeProvider? time = null,
    ILogger<SimLeague>? logger = null) : ILeagueReader, ILeagueWriter
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ILogger<SimLeague> _logger = logger ?? NullLogger<SimLeague>.Instance;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile SimStateDocument? _state;

    private string StatePath => Path.Combine(stateDirectory, "state.json");

    /// <summary>Restores the seeded state from a snapshot (latest when id is null).</summary>
    public async Task ResetAsync(string? snapshotId, CancellationToken cancellationToken)
    {
        using var activity = LeagueDiagnostics.ActivitySource.StartActivity("sim.reset");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = snapshotId is null
                ? await snapshots.LoadLatestAsync(cancellationToken)
                : await snapshots.LoadAsync(snapshotId, cancellationToken);

            var seeded = SimStateDocument.FromSnapshot(
                snapshot ?? throw new InvalidOperationException(
                    $"No snapshot {(snapshotId is null ? "available" : $"'{snapshotId}'")} to seed SimLeague from."));
            await PersistAsync(seeded, cancellationToken);
            _state = seeded;
            _logger.LogInformation(
                "SimLeague reset from snapshot {SnapshotId}.", seeded.SeededFromSnapshotId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Lineup> SetLineupAsync(
        string franchiseId,
        int week,
        IReadOnlyList<string> starterPlayerIds,
        CancellationToken cancellationToken)
    {
        using var activity = StartWrite("sim.set_lineup", franchiseId);
        return await MutateAsync(state =>
        {
            var roster = RequireRoster(state, franchiseId);
            var offRoster = starterPlayerIds.Except(roster.Slots.Select(s => s.PlayerId)).ToList();
            if (offRoster.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot start players not on franchise {franchiseId}'s roster: {string.Join(", ", offRoster)}.");
            }

            var lineup = new Lineup(franchiseId, week, [.. starterPlayerIds]);
            var next = state with
            {
                Lineups =
                [
                    .. state.Lineups.Where(l => !(l.FranchiseId == franchiseId && l.Week == week)),
                    lineup,
                ],
            };
            next = Log(next, TransactionType.Lineup, franchiseId,
                $"Set week {week} lineup ({starterPlayerIds.Count} starters)", starterPlayerIds);
            return (next, lineup);
        }, cancellationToken);
    }

    public async Task<Trade> ProposeTradeAsync(
        string fromFranchiseId,
        string toFranchiseId,
        IReadOnlyList<string> give,
        IReadOnlyList<string> get,
        string note,
        CancellationToken cancellationToken)
    {
        using var activity = StartWrite("sim.propose_trade", fromFranchiseId);
        return await MutateAsync(state =>
        {
            if (fromFranchiseId == toFranchiseId)
            {
                // Accepting a self-trade would duplicate roster slots.
                throw new InvalidOperationException("A franchise cannot trade with itself.");
            }

            RequireOnRoster(state, fromFranchiseId, give, "give");
            RequireOnRoster(state, toFranchiseId, get, "get");

            // The note is untrusted free text (the Friday injection vector): stored verbatim,
            // treated as data everywhere.
            var trade = new Trade(
                $"T{state.NextTradeNumber:0000}", fromFranchiseId, toFranchiseId,
                [.. give], [.. get], note, TradeStatus.Pending, _time.GetUtcNow());

            var next = state with
            {
                NextTradeNumber = state.NextTradeNumber + 1,
                Trades = [.. state.Trades, trade],
            };
            next = Log(next, TransactionType.TradeProposed, fromFranchiseId,
                $"Proposed trade {trade.Id} to {toFranchiseId}", [.. give, .. get]);
            return (next, trade);
        }, cancellationToken);
    }

    public async Task<Trade> RespondToTradeAsync(
        string tradeId,
        bool accept,
        string respondingFranchiseId,
        CancellationToken cancellationToken)
    {
        using var activity = StartWrite("sim.respond_to_trade", respondingFranchiseId);
        return await MutateAsync(state =>
        {
            var trade = state.Trades.FirstOrDefault(t => t.Id == tradeId)
                ?? throw new KeyNotFoundException($"No trade '{tradeId}'.");
            if (trade.Status != TradeStatus.Pending)
            {
                throw new InvalidOperationException($"Trade {tradeId} is already {trade.Status}.");
            }

            if (trade.ToFranchiseId != respondingFranchiseId)
            {
                throw new InvalidOperationException(
                    $"Trade {tradeId} was offered to {trade.ToFranchiseId}, not {respondingFranchiseId}.");
            }

            var next = state;
            var resolved = trade with { Status = accept ? TradeStatus.Accepted : TradeStatus.Rejected };
            if (accept)
            {
                RequireOnRoster(state, trade.FromFranchiseId, trade.Give, "give");
                RequireOnRoster(state, trade.ToFranchiseId, trade.Get, "get");
                next = MovePlayers(next, trade.FromFranchiseId, trade.ToFranchiseId, trade.Give);
                next = MovePlayers(next, trade.ToFranchiseId, trade.FromFranchiseId, trade.Get);
            }

            next = next with
            {
                Trades = [.. next.Trades.Where(t => t.Id != tradeId), resolved],
            };
            next = Log(
                next,
                accept ? TransactionType.TradeAccepted : TransactionType.TradeRejected,
                respondingFranchiseId,
                $"{(accept ? "Accepted" : "Rejected")} trade {tradeId} from {trade.FromFranchiseId}",
                [.. trade.Give, .. trade.Get]);
            return (next, resolved);
        }, cancellationToken);
    }

    public async Task DropPlayerAsync(string franchiseId, string playerId, CancellationToken cancellationToken)
    {
        using var activity = StartWrite("sim.drop_player", franchiseId);
        await MutateAsync(state =>
        {
            var roster = RequireRoster(state, franchiseId);
            if (roster.Slots.All(s => s.PlayerId != playerId))
            {
                throw new InvalidOperationException(
                    $"Player {playerId} is not on franchise {franchiseId}'s roster.");
            }

            var next = ReplaceRoster(state, franchiseId,
                [.. roster.Slots.Where(s => s.PlayerId != playerId)]);
            next = Log(next, TransactionType.Drop, franchiseId, $"Dropped player {playerId}", [playerId]);
            return (next, playerId);
        }, cancellationToken);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<SimStateDocument, (SimStateDocument Next, TResult Result)> mutate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var (next, result) = mutate(await RequireStateLockedAsync(cancellationToken));
            // Persist first: a failed save must leave readers on the previous state.
            await PersistAsync(next, cancellationToken);
            _state = next;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private SimStateDocument Log(
        SimStateDocument state,
        TransactionType type,
        string franchiseId,
        string description,
        IReadOnlyList<string> playerIds)
        => state with
        {
            NextSequence = state.NextSequence + 1,
            TransactionLog =
            [
                .. state.TransactionLog,
                new Transaction(state.NextSequence, _time.GetUtcNow(), type, franchiseId, description, playerIds),
            ],
        };

    private static SimStateDocument MovePlayers(
        SimStateDocument state, string fromId, string toId, IReadOnlyList<string> playerIds)
    {
        var from = RequireRoster(state, fromId);
        var to = RequireRoster(state, toId);
        var moving = from.Slots.Where(s => playerIds.Contains(s.PlayerId)).ToList();

        state = ReplaceRoster(state, fromId, [.. from.Slots.Where(s => !playerIds.Contains(s.PlayerId))]);
        return ReplaceRoster(state, toId, [.. to.Slots, .. moving]);
    }

    private static SimStateDocument ReplaceRoster(
        SimStateDocument state, string franchiseId, IReadOnlyList<RosterSlot> slots)
        => state with
        {
            Rosters =
            [
                .. state.Rosters.Select(r => r.FranchiseId == franchiseId ? new Roster(franchiseId, slots) : r),
            ],
        };

    private static Roster RequireRoster(SimStateDocument state, string franchiseId)
        => state.Franchises.Any(f => f.Id == franchiseId)
            ? state.Rosters.FirstOrDefault(r => r.FranchiseId == franchiseId) ?? new Roster(franchiseId, [])
            : throw new KeyNotFoundException($"No franchise '{franchiseId}' in the simulated league.");

    private static void RequireOnRoster(
        SimStateDocument state, string franchiseId, IReadOnlyList<string> playerIds, string side)
    {
        var roster = RequireRoster(state, franchiseId);
        var missing = playerIds.Except(roster.Slots.Select(s => s.PlayerId)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Trade {side} players not on franchise {franchiseId}'s roster: {string.Join(", ", missing)}.");
        }
    }

    private static System.Diagnostics.Activity? StartWrite(string name, string franchiseId)
    {
        var activity = LeagueDiagnostics.ActivitySource.StartActivity(name);
        activity?.SetTag("league.franchise_id", franchiseId);
        return activity;
    }
}
