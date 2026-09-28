using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Sim;

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
    IReadOnlyList<Standing> Standings,
    IReadOnlyList<Lineup> Lineups,
    IReadOnlyList<Transaction> TransactionLog,
    IReadOnlyList<Trade> Trades)
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
        snapshot.Standings,
        Lineups: [],
        TransactionLog: snapshot.Transactions,
        Trades: snapshot.PendingTrades);
}
