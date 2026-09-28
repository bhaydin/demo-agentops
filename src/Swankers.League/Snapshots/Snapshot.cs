using Swankers.League.Models;

namespace Swankers.League.Snapshots;

/// <summary>
/// Snapshot metadata. Deliberately excludes the MFL league id and any owner identity: a
/// snapshot on disk contains only normalized domain data with display names from
/// data/franchise-names.json.
/// </summary>
public sealed record SnapshotManifest(
    string Id,
    DateTimeOffset CapturedAtUtc,
    int Week,
    string Source);

/// <summary>
/// A complete normalized league snapshot. Snapshots are allow-listed by construction:
/// these domain records are the only thing ever serialized, so MFL-side fields (including
/// the owner PII in the league export) cannot reach disk.
/// </summary>
public sealed record Snapshot(
    SnapshotManifest Manifest,
    IReadOnlyList<Franchise> Franchises,
    IReadOnlyList<Player> Players,
    IReadOnlyList<Roster> Rosters,
    IReadOnlyList<Injury> Injuries,
    IReadOnlyList<Matchup> Matchups,
    IReadOnlyList<Projection> Projections,
    IReadOnlyList<WeeklyResult> WeeklyResults,
    IReadOnlyList<Standing> Standings,
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyList<Trade> PendingTrades)
{
    public static Snapshot Empty(SnapshotManifest manifest)
        => new(manifest, [], [], [], [], [], [], [], [], [], []);
}
