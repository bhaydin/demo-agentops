using Swankers.League.Models;

namespace Swankers.League.Snapshots;

/// <summary>
/// ILeagueReader over the latest snapshot on disk. Used as MflExportClient's offline fallback;
/// an empty store yields empty results rather than errors so a demo degrades instead of dying.
/// </summary>
public sealed class SnapshotLeagueReader(SnapshotStore store) : ISnapshotLeagueReader
{
    private Snapshot? _snapshot;

    public async Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Franchises;

    public async Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Players;

    public async Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Rosters;

    public async Task<Roster> GetRosterAsync(string franchiseId, CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Rosters.FirstOrDefault(r => r.FranchiseId == franchiseId)
            ?? new Roster(franchiseId, []);

    public async Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Injuries;

    public async Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken ct)
        => [.. (await GetSnapshotAsync(ct)).Matchups.Where(m => m.Week == week)];

    public async Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken ct)
        => [.. (await GetSnapshotAsync(ct)).Projections.Where(p => p.Week == week)];

    public async Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken ct)
        => [.. (await GetSnapshotAsync(ct)).WeeklyResults.Where(r => r.Week == week)];

    public async Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Standings;

    public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken ct)
        => [.. (await GetSnapshotAsync(ct)).Transactions.TakeLast(count)];

    public async Task<IReadOnlyList<Trade>> GetPendingTradesAsync(string franchiseId, CancellationToken ct)
        => [.. (await GetSnapshotAsync(ct)).PendingTrades
            .Where(t => t.FromFranchiseId == franchiseId || t.ToFranchiseId == franchiseId)];

    public async Task<SnapshotManifest?> GetManifestAsync(CancellationToken ct)
        => (await GetSnapshotAsync(ct)).Manifest is { Source: not "none" } manifest ? manifest : null;

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
        => _snapshot ??= await store.LoadLatestAsync(ct)
            ?? Snapshot.Empty(new SnapshotManifest("empty", DateTimeOffset.MinValue, 0, "none"));
}
