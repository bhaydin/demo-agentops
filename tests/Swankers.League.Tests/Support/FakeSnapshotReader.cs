using Swankers.League;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Tests.Support;

/// <summary>Canned ILeagueReader used as the snapshot fallback in client tests.</summary>
public sealed class FakeSnapshotReader : ISnapshotLeagueReader
{
    public List<Franchise> Franchises { get; init; } = [];
    public List<Player> Players { get; init; } = [];
    public List<Roster> Rosters { get; init; } = [];
    public List<Injury> Injuries { get; init; } = [];
    public List<Matchup> Matchups { get; init; } = [];
    public List<Projection> Projections { get; init; } = [];
    public List<WeeklyResult> WeeklyResults { get; init; } = [];
    public List<Standing> Standings { get; init; } = [];
    public List<Transaction> Transactions { get; init; } = [];
    public List<Trade> PendingTrades { get; init; } = [];
    public SnapshotManifest? Manifest { get; init; }

    public Task<SnapshotManifest?> GetManifestAsync(CancellationToken ct) => Task.FromResult(Manifest);

    public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken ct)
        => Result(Franchises);

    public Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken ct) => Result(Players);

    public Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken ct) => Result(Rosters);

    public Task<Roster> GetRosterAsync(string franchiseId, CancellationToken ct)
        => Task.FromResult(Rosters.FirstOrDefault(r => r.FranchiseId == franchiseId)
            ?? new Roster(franchiseId, []));

    public Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken ct)
        => Result(Injuries);

    public Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken ct)
        => Result([.. Matchups.Where(m => m.Week == week)]);

    public Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken ct)
        => Result([.. Projections.Where(p => p.Week == week)]);

    public Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken ct)
        => Result([.. WeeklyResults.Where(r => r.Week == week)]);

    public Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken ct)
        => Result(Standings);

    public Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken ct)
        => Result(Transactions);

    public Task<IReadOnlyList<Trade>> GetPendingTradesAsync(string franchiseId, CancellationToken ct)
        => Result(PendingTrades);

    private static Task<IReadOnlyList<TItem>> Result<TItem>(List<TItem> items)
        => Task.FromResult<IReadOnlyList<TItem>>(items);
}
