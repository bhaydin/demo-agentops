using System.Text.Json;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Sim;

/// <summary>ILeagueReader over the simulated state, plus state loading and persistence.</summary>
public sealed partial class SimLeague
{
    public async Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken ct)
        => (await RequireStateAsync(ct)).Franchises;

    public async Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken ct)
        => (await RequireStateAsync(ct)).Players;

    public async Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken ct)
        => (await RequireStateAsync(ct)).Rosters;

    public async Task<Roster> GetRosterAsync(string franchiseId, CancellationToken ct)
        => (await RequireStateAsync(ct)).Rosters.FirstOrDefault(r => r.FranchiseId == franchiseId)
            ?? new Roster(franchiseId, []);

    public async Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken ct)
        => (await RequireStateAsync(ct)).Injuries;

    public async Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken ct)
        => [.. (await RequireStateAsync(ct)).Matchups.Where(m => m.Week == week)];

    public async Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken ct)
        => [.. (await RequireStateAsync(ct)).Projections.Where(p => p.Week == week)];

    public async Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken ct)
        => [.. (await RequireStateAsync(ct)).WeeklyResults.Where(r => r.Week == week)];

    public async Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken ct)
        => (await RequireStateAsync(ct)).Standings;

    public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken ct)
        => [.. (await RequireStateAsync(ct)).TransactionLog.TakeLast(count)];

    public async Task<IReadOnlyList<Trade>> GetPendingTradesAsync(string franchiseId, CancellationToken ct)
        => [.. (await RequireStateAsync(ct)).Trades.Where(t =>
            t.Status == TradeStatus.Pending &&
            (t.FromFranchiseId == franchiseId || t.ToFranchiseId == franchiseId))];

    /// <summary>The declared lineup, if any, for a franchise and week (used by the web ticker).</summary>
    public async Task<Lineup?> GetLineupAsync(string franchiseId, int week, CancellationToken ct)
        => (await RequireStateAsync(ct)).Lineups
            .FirstOrDefault(l => l.FranchiseId == franchiseId && l.Week == week);

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
