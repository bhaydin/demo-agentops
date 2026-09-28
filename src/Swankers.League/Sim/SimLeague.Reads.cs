using System.Diagnostics;
using System.Text.Json;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Sim;

/// <summary>
/// ILeagueReader over the simulated state, plus state loading and persistence. Every read is
/// a <c>sim.*</c> span under Swankers.League so a tool call's trace shows the league hop.
/// </summary>
public sealed partial class SimLeague
{
    public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken ct)
        => ReadAsync("get_franchises", s => s.Franchises, ct);

    public Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken ct)
        => ReadAsync("get_players", s => s.Players, ct);

    public Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken ct)
        => ReadAsync("get_rosters", s => s.Rosters, ct);

    public Task<Roster> GetRosterAsync(string franchiseId, CancellationToken ct)
        => ReadAsync("get_roster",
            s => s.Rosters.FirstOrDefault(r => r.FranchiseId == franchiseId) ?? new Roster(franchiseId, []),
            ct, franchiseId);

    public Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken ct)
        => ReadAsync("get_injuries", s => s.Injuries, ct);

    public Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken ct)
        => ReadAsync<IReadOnlyList<Matchup>>("get_matchups", s => [.. s.Matchups.Where(m => m.Week == week)], ct);

    public Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken ct)
        => ReadAsync<IReadOnlyList<Projection>>("get_projections", s => [.. s.Projections.Where(p => p.Week == week)], ct);

    public Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken ct)
        => ReadAsync<IReadOnlyList<WeeklyResult>>("get_weekly_results", s => [.. s.WeeklyResults.Where(r => r.Week == week)], ct);

    public Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken ct)
        => ReadAsync("get_standings", s => s.Standings, ct);

    public Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken ct)
        => ReadAsync<IReadOnlyList<Transaction>>("get_transactions", s => [.. s.TransactionLog.TakeLast(count)], ct);

    public Task<IReadOnlyList<Trade>> GetPendingTradesAsync(string franchiseId, CancellationToken ct)
        => ReadAsync<IReadOnlyList<Trade>>("get_pending_trades",
            s => [.. s.Trades.Where(t =>
                t.Status == TradeStatus.Pending &&
                (t.FromFranchiseId == franchiseId || t.ToFranchiseId == franchiseId))],
            ct, franchiseId);

    /// <summary>A trade by id in any status, or null.</summary>
    public Task<Trade?> GetTradeAsync(string tradeId, CancellationToken ct)
        => ReadAsync("get_trade", s => s.Trades.FirstOrDefault(t => t.Id == tradeId), ct);

    /// <summary>The league's current week, as seeded from the snapshot.</summary>
    public Task<int> GetCurrentWeekAsync(CancellationToken ct)
        => ReadAsync("get_current_week", s => s.Week, ct);

    /// <summary>The declared lineup, if any, for a franchise and week (used by the web ticker).</summary>
    public Task<Lineup?> GetLineupAsync(string franchiseId, int week, CancellationToken ct)
        => ReadAsync("get_lineup",
            s => s.Lineups.FirstOrDefault(l => l.FranchiseId == franchiseId && l.Week == week),
            ct, franchiseId);

    private async Task<T> ReadAsync<T>(
        string operation,
        Func<SimStateDocument, T> project,
        CancellationToken cancellationToken,
        string? franchiseId = null)
    {
        using var activity = LeagueDiagnostics.ActivitySource.StartActivity($"sim.{operation}");
        if (franchiseId is not null)
        {
            activity?.SetTag("league.franchise_id", franchiseId);
        }

        var state = await RequireStateAsync(cancellationToken);
        activity?.SetTag("league.snapshot_id", state.SeededFromSnapshotId);
        return project(state);
    }

    /// <summary>
    /// Returns the current state, initializing under the write gate on first use so concurrent
    /// cold reads cannot race each other (or a write) to create state.json. Once loaded, reads
    /// are lock-free: the document is immutable and the field assignment is atomic.
    /// </summary>
    private async Task<SimStateDocument> RequireStateAsync(CancellationToken cancellationToken)
    {
        if (_state is { } loaded)
        {
            return loaded;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RequireStateLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads persisted state or seeds from the latest snapshot. The caller holds the gate.</summary>
    private async Task<SimStateDocument> RequireStateLockedAsync(CancellationToken cancellationToken)
    {
        if (_state is { } loaded)
        {
            return loaded;
        }

        if (File.Exists(StatePath))
        {
            await using var stream = File.OpenRead(StatePath);
            var persisted = await JsonSerializer.DeserializeAsync<SimStateDocument>(
                stream, SnapshotStore.JsonOptions, cancellationToken);
            if (persisted is not null)
            {
                _state = persisted;
                return persisted;
            }
        }

        var snapshot = await snapshots.LoadLatestAsync(cancellationToken)
            ?? throw new InvalidOperationException("No snapshot available to seed SimLeague from.");
        var seeded = SimStateDocument.FromSnapshot(snapshot);
        await PersistAsync(seeded, cancellationToken);
        _state = seeded;
        return seeded;
    }

    /// <summary>Writes a candidate document atomically. Callers publish it to _state only after this succeeds.</summary>
    private async Task PersistAsync(SimStateDocument candidate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stateDirectory);
        var tempPath = StatePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, candidate, SnapshotStore.JsonOptions, cancellationToken);
        }

        File.Move(tempPath, StatePath, overwrite: true);
    }
}
