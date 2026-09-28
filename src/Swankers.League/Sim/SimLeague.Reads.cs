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
    /// Loads persisted state, or seeds from the latest snapshot. Never takes the write gate:
    /// MutateAsync calls this while already holding it. Assigning the immutable document is
    /// atomic, so lock-free reads at worst see the previous state.
    /// </summary>
    private async Task<SimStateDocument> RequireStateAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return _state;
        }

        if (File.Exists(StatePath))
        {
            await using var stream = File.OpenRead(StatePath);
            _state = await JsonSerializer.DeserializeAsync<SimStateDocument>(
                stream, SnapshotStore.JsonOptions, cancellationToken);
            if (_state is not null)
            {
                return _state;
            }
        }

        var snapshot = await snapshots.LoadLatestAsync(cancellationToken)
            ?? throw new InvalidOperationException("No snapshot available to seed SimLeague from.");
        _state = SimStateDocument.FromSnapshot(snapshot);
        await PersistAsync(cancellationToken);
        return _state;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stateDirectory);
        var tempPath = StatePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _state, SnapshotStore.JsonOptions, cancellationToken);
        }

        File.Move(tempPath, StatePath, overwrite: true);
    }
}
