using Microsoft.Extensions.Logging;
using Swankers.League;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.SnapshotCapture;

/// <summary>
/// Pulls exports through MflExportClient, normalizes them into domain records (which
/// structurally cannot carry owner PII), adds The Fleecers as a sim-only franchise, and
/// writes the snapshot. Pending trades are deliberately NOT captured: real trade notes
/// could identify league members. Demo trades come from data/demo scenarios.
/// </summary>
public sealed class CaptureService(
    ILeagueReader mfl,
    SnapshotStore store,
    CaptureOptions options,
    ILogger<CaptureService> logger)
{
    internal const string FleecersId = "0099";

    public async Task<string> CaptureAsync(CancellationToken cancellationToken)
    {
        var id = options.SnapshotId ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        logger.LogInformation("Capturing snapshot {SnapshotId} (week {Week})...", id, options.Week);

        var franchises = await mfl.GetFranchisesAsync(cancellationToken);
        var players = await mfl.GetPlayersAsync(cancellationToken);
        var rosters = await mfl.GetRostersAsync(cancellationToken);
        var injuries = await mfl.GetInjuriesAsync(options.Week, cancellationToken);
        var projections = await mfl.GetProjectionsAsync(options.Week, cancellationToken);
        var standings = await mfl.GetStandingsAsync(cancellationToken);
        var transactions = await mfl.GetTransactionsAsync(options.TransactionCount, cancellationToken);

        var matchups = new List<Matchup>();
        for (var week = 1; week <= options.Week; week++)
        {
            matchups.AddRange(await mfl.GetMatchupsAsync(week, cancellationToken));
        }

        var fleecersRoster = BuildFleecersRoster(players, rosters, projections);
        var snapshot = new Snapshot(
            new SnapshotManifest(id, DateTimeOffset.UtcNow, options.Week, "mfl"),
            [.. franchises, new Franchise(FleecersId, "The Fleecers", IsSimOnly: true)],
            players,
            [.. rosters, fleecersRoster],
            injuries,
            matchups,
            projections,
            standings,
            transactions,
            PendingTrades: []);

        await store.SaveAsync(snapshot, cancellationToken);
        logger.LogInformation(
            "Snapshot {SnapshotId}: {Franchises} franchises, {Players} players, {Rosters} rosters, "
            + "{Matchups} matchups, {Fleecers} Fleecers players.",
            id, snapshot.Franchises.Count, players.Count, snapshot.Rosters.Count,
            matchups.Count, fleecersRoster.Slots.Count);
        return id;
    }

    /// <summary>
    /// The Fleecers draft from free agents at capture time: best projected available, with a
    /// position mix that yields a startable roster. Deterministic for a given capture.
    /// </summary>
    internal static Roster BuildFleecersRoster(
        IReadOnlyList<Player> players,
        IReadOnlyList<Roster> rosters,
        IReadOnlyList<Projection> projections)
    {
        var rostered = rosters.SelectMany(r => r.Slots).Select(s => s.PlayerId).ToHashSet();
        var points = projections.ToDictionary(p => p.PlayerId, p => p.Points);

        var freeAgents = players
            .Where(p => !rostered.Contains(p.Id))
            .OrderByDescending(p => points.GetValueOrDefault(p.Id))
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        var quotas = new Dictionary<string, int> { ["QB"] = 2, ["RB"] = 4, ["WR"] = 4, ["TE"] = 2 };
        var picked = new List<Player>();
        foreach (var (position, quota) in quotas)
        {
            picked.AddRange(freeAgents.Where(p => p.Position == position).Take(quota));
        }

        picked.AddRange(freeAgents.Except(picked).Take(15 - picked.Count));

        return new Roster(FleecersId, [.. picked.Select(p => new RosterSlot(p.Id, RosterStatus.Roster))]);
    }
}
