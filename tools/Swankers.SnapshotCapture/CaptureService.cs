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

        // Only weeks whose every matchup has a final score count as completed.
        var completedWeeks = matchups
            .GroupBy(m => m.Week)
            .Where(g => g.All(m => m.HomeScore is not null && m.AwayScore is not null))
            .Select(g => g.Key)
            .Order()
            .ToList();

        var weeklyResults = new List<WeeklyResult>();
        foreach (var week in completedWeeks)
        {
            var results = await mfl.GetWeeklyResultsAsync(week, cancellationToken);
            if (results.Count == 0 || results.All(r => r.Players.Count == 0))
            {
                logger.LogWarning(
                    "weeklyResults for completed week {Week} parsed to no player rows; the response shape may differ from the parser.",
                    week);
            }

            weeklyResults.AddRange(results);
        }

        WarnIfEmpty("players", players.Count);
        WarnIfEmpty("rosters", rosters.Count);
        WarnIfEmpty("projections", projections.Count);
        WarnIfEmpty("standings", standings.Count);
        WarnIfEmpty("matchups", matchups.Count);

        var fleecersRoster = BuildFleecersRoster(players, rosters, projections);
        var snapshot = new Snapshot(
            new SnapshotManifest(id, DateTimeOffset.UtcNow, options.Week, "mfl"),
            [.. franchises, new Franchise(FleecersId, "The Fleecers", IsSimOnly: true)],
            players,
            [.. rosters, fleecersRoster],
            injuries,
            matchups,
            projections,
            weeklyResults,
            standings,
            transactions,
            PendingTrades: []);

        await store.SaveAsync(snapshot, cancellationToken);
        logger.LogInformation(
            "Snapshot {SnapshotId}: {Franchises} franchises, {Players} players, {Rosters} rosters, "
            + "{Matchups} matchups, completed weeks [{Completed}], {Results} weekly results, "
            + "{Transactions} transactions, {Fleecers} Fleecers players.",
            id, snapshot.Franchises.Count, players.Count, snapshot.Rosters.Count, matchups.Count,
            string.Join(",", completedWeeks), weeklyResults.Count, transactions.Count,
            fleecersRoster.Slots.Count);
        return id;
    }

    private void WarnIfEmpty(string what, int count)
    {
        if (count == 0)
        {
            logger.LogWarning("{What} parsed to zero rows; the response shape may differ from the parser.", what);
        }
    }

    /// <summary>
    /// Position mix of a typical Swankers roster (measured from the 2026-09-28 capture).
    /// MFL position codes: PK = kicker, Def = team defense.
    /// </summary>
    internal static readonly IReadOnlyList<(string Position, int Count)> FleecersQuotas =
    [
        ("QB", 2), ("RB", 5), ("WR", 5), ("TE", 2), ("PK", 1), ("Def", 1),
    ];

    /// <summary>
    /// The Fleecers draft from free agents at capture time: best projected available at each
    /// position, in the league's typical roster shape. Deterministic for a given capture.
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

        var picked = FleecersQuotas
            .SelectMany(q => freeAgents.Where(p => p.Position == q.Position).Take(q.Count));

        return new Roster(FleecersId, [.. picked.Select(p => new RosterSlot(p.Id, RosterStatus.Roster))]);
    }
}
