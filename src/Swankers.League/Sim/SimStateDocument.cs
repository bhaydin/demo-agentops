using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Sim;

/// <summary>Provenance of the simulated state: the seeding snapshot and its capture time.</summary>
public sealed record SimSeed(string SnapshotId, DateTimeOffset? CapturedAtUtc);

/// <summary>
/// The whole simulated league as one immutable, JSON-persisted document. Every SimLeague
/// write produces the next document, so reset can restore the exact seeded state.
/// </summary>
public sealed record SimStateDocument(
    string SeededFromSnapshotId,
    int Week,
    long NextSequence,
    int NextTradeNumber,
    IReadOnlyList<Franchise> Franchises,
    IReadOnlyList<Player> Players,
    IReadOnlyList<Roster> Rosters,
    IReadOnlyList<Injury> Injuries,
    IReadOnlyList<Matchup> Matchups,
    IReadOnlyList<Projection> Projections,
    IReadOnlyList<WeeklyResult> WeeklyResults,
    IReadOnlyList<Standing> Standings,
    IReadOnlyList<Lineup> Lineups,
    IReadOnlyList<Transaction> TransactionLog,
    IReadOnlyList<Trade> Trades,
    DateTimeOffset? SnapshotCapturedAtUtc = null)
{
    public static SimStateDocument FromSnapshot(Snapshot snapshot) => new(
        snapshot.Manifest.Id,
        snapshot.Manifest.Week,
        NextSequence: snapshot.Transactions.Count == 0 ? 1 : snapshot.Transactions.Max(t => t.Sequence) + 1,
        NextTradeNumber: 1,
        snapshot.Franchises,
        snapshot.Players,
        snapshot.Rosters,
        snapshot.Injuries,
        snapshot.Matchups,
        snapshot.Projections,
        snapshot.WeeklyResults,
        snapshot.Standings,
        Lineups: [],
        TransactionLog: snapshot.Transactions,
        Trades: snapshot.PendingTrades,
        SnapshotCapturedAtUtc: snapshot.Manifest.CapturedAtUtc);
}
