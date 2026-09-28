using System.Text.Json;
using Swankers.League.Models;

namespace Swankers.League.Mfl;

/// <summary>
/// Response parsing. All parsers tolerate MFL's quirks via <see cref="MflJson"/> and return
/// empty collections rather than throw on unexpected shapes. players/injuries/league shapes
/// were verified against live responses (2026-09-27); the rest follow the api_info docs and
/// are validated against the maintainer's real capture at the Phase 1 gate.
/// </summary>
public sealed partial class MflExportClient
{
    private IReadOnlyList<Franchise> ParseFranchises(JsonElement root)
    {
        // Reads franchise ids ONLY. The league export also carries owner PII (name, email,
        // phone, address); those properties are deliberately never accessed, and names are
        // resolved through the maintainer-controlled FranchiseNameMap.
        var league = root.TryGetProperty("league", out var l) ? l : default;
        var franchises = league.ValueKind == JsonValueKind.Object &&
            league.TryGetProperty("franchises", out var f) ? f : default;

        return
        [
            .. MflJson.Elements(franchises, "franchise")
                .Select(e => MflJson.GetString(e, "id"))
                .Where(id => id.Length > 0)
                .Select(id => new Franchise(id, _names.Resolve(id)))
        ];
    }

    private static IReadOnlyList<Player> ParsePlayers(JsonElement root)
        => root.TryGetProperty("players", out var players)
            ?
            [
                .. MflJson.Elements(players, "player")
                    .Select(e => new Player(
                        MflJson.GetString(e, "id"),
                        MflJson.GetString(e, "name"),
                        MflJson.GetString(e, "position"),
                        MflJson.GetString(e, "team")))
                    .Where(p => p.Id.Length > 0)
            ]
            : [];

    private static IReadOnlyList<Injury> ParseInjuries(JsonElement root)
        => root.TryGetProperty("injuries", out var injuries)
            ?
            [
                .. MflJson.Elements(injuries, "injury")
                    .Select(e => new Injury(
                        MflJson.GetString(e, "id"),
                        MflJson.GetString(e, "status"),
                        MflJson.GetString(e, "details"),
                        MflJson.GetString(e, "exp_return") is { Length: > 0 } r ? r : null))
                    .Where(i => i.PlayerId.Length > 0)
            ]
            : [];

    private static IReadOnlyList<Roster> ParseRosters(JsonElement root)
    {
        if (!root.TryGetProperty("rosters", out var rosters))
        {
            return [];
        }

        return
        [
            .. MflJson.Elements(rosters, "franchise").Select(franchise => new Roster(
                MflJson.GetString(franchise, "id"),
                [
                    .. MflJson.Elements(franchise, "player").Select(p => new RosterSlot(
                        MflJson.GetString(p, "id"),
                        ParseRosterStatus(MflJson.GetString(p, "status"))))
                ]))
        ];
    }

    private static RosterStatus ParseRosterStatus(string status) => status.ToUpperInvariant() switch
    {
        "ROSTER" or "STARTER" or "NONSTARTER" => RosterStatus.Roster,
        "TAXI_SQUAD" or "TAXI" => RosterStatus.TaxiSquad,
        "INJURED_RESERVE" or "IR" => RosterStatus.InjuredReserve,
        _ => RosterStatus.Unknown,
    };

    private static IReadOnlyList<Matchup> ParseMatchups(JsonElement root, int week)
    {
        if (!root.TryGetProperty("schedule", out var schedule))
        {
            return [];
        }

        var results = new List<Matchup>();
        foreach (var weekly in MflJson.Elements(schedule, "weeklySchedule"))
        {
            if (MflJson.GetInt(weekly, "week") != week)
            {
                continue;
            }

            foreach (var matchup in MflJson.Elements(weekly, "matchup"))
            {
                var sides = MflJson.Elements(matchup, "franchise").ToList();
                if (sides.Count != 2)
                {
                    continue;
                }

                // Prefer the isHome flag when present; otherwise keep MFL's order.
                var homeIndex = sides.FindIndex(s => MflJson.GetString(s, "isHome") == "1");
                if (homeIndex < 0)
                {
                    homeIndex = 0;
                }

                var home = sides[homeIndex];
                var away = sides[homeIndex == 0 ? 1 : 0];

                results.Add(new Matchup(
                    week,
                    MflJson.GetString(home, "id"),
                    MflJson.GetString(away, "id"),
                    MflJson.GetDecimal(home, "score"),
                    MflJson.GetDecimal(away, "score")));
            }
        }

        return results;
    }

    private static IReadOnlyList<Projection> ParseProjections(JsonElement root, int week)
        => root.TryGetProperty("projectedScores", out var scores)
            ?
            [
                .. MflJson.Elements(scores, "playerScore")
                    .Select(e => new Projection(
                        MflJson.GetString(e, "id"),
                        week,
                        MflJson.GetDecimal(e, "score") ?? 0m))
                    .Where(p => p.PlayerId.Length > 0)
            ]
            : [];

    private static IReadOnlyList<Standing> ParseStandings(JsonElement root)
        => root.TryGetProperty("leagueStandings", out var standings)
            ?
            [
                .. MflJson.Elements(standings, "franchise")
                    .Select(e => new Standing(
                        MflJson.GetString(e, "id"),
                        MflJson.GetInt(e, "h2hw") ?? 0,
                        MflJson.GetInt(e, "h2hl") ?? 0,
                        MflJson.GetInt(e, "h2ht") ?? 0,
                        MflJson.GetDecimal(e, "pf") ?? 0m,
                        MflJson.GetDecimal(e, "pa") ?? 0m))
                    .Where(s => s.FranchiseId.Length > 0)
            ]
            : [];

    /// <summary>
    /// weeklyResults: franchises appear under matchup[].franchise[] (and top-level franchise[]
    /// for teams outside a head-to-head pairing), each with a score and player[] rows
    /// (id, score, status "starter"/"nonstarter"). Shape per the MFL docs; checked against
    /// the maintainer's capture, which warns if a completed week parses to zero rows.
    /// </summary>
    private static IReadOnlyList<WeeklyResult> ParseWeeklyResults(JsonElement root, int week)
    {
        if (!root.TryGetProperty("weeklyResults", out var results))
        {
            return [];
        }

        var franchises = MflJson.Elements(results, "matchup")
            .SelectMany(m => MflJson.Elements(m, "franchise"))
            .Concat(MflJson.Elements(results, "franchise"));

        return
        [
            .. franchises
                .Select(f => new WeeklyResult(
                    week,
                    MflJson.GetString(f, "id"),
                    MflJson.GetDecimal(f, "score") ?? 0m,
                    [
                        .. MflJson.Elements(f, "player").Select(p => new PlayerResult(
                            MflJson.GetString(p, "id"),
                            MflJson.GetDecimal(p, "score") ?? 0m,
                            MflJson.GetString(p, "status").Equals("starter", StringComparison.OrdinalIgnoreCase)))
                    ]))
                .Where(r => r.FranchiseId.Length > 0)
                .DistinctBy(r => r.FranchiseId)
        ];
    }

    /// <summary>
    /// transactions: add/drop entries encode players as "added,ids,|dropped,ids," (verified
    /// against live data 2026-09-28). League-level system entries carry no franchise and are
    /// skipped. MFL lists newest first; results are returned oldest first so sequence numbers
    /// increase over time, matching SimLeague's append-only log.
    /// </summary>
    private static IReadOnlyList<Transaction> ParseTransactions(JsonElement root)
    {
        if (!root.TryGetProperty("transactions", out var transactions))
        {
            return [];
        }

        var parsed = MflJson.Elements(transactions, "transaction")
            .Select(e => (
                Franchise: MflJson.GetString(e, "franchise"),
                Type: MflJson.GetString(e, "type"),
                Raw: MflJson.GetString(e, "transaction"),
                Timestamp: DateTimeOffset.FromUnixTimeSeconds(MflJson.GetInt(e, "timestamp") ?? 0)))
            .Where(t => t.Franchise.Length > 0)
            .OrderBy(t => t.Timestamp)
            .ToList();

        var sequence = 0L;
        return [.. parsed.Select(t => ToTransaction(++sequence, t.Franchise, t.Type, t.Raw, t.Timestamp))];
    }

    private static Transaction ToTransaction(
        long sequence, string franchise, string type, string raw, DateTimeOffset timestamp)
    {
        if (type.Contains("TRADE", StringComparison.OrdinalIgnoreCase))
        {
            return new Transaction(sequence, timestamp, TransactionType.TradeAccepted, franchise, "Trade", []);
        }

        var halves = raw.Split('|');
        var added = SplitAssets(halves[0]);
        var dropped = halves.Length > 1 ? SplitAssets(halves[1]) : [];

        var kind = added.Count > 0 ? TransactionType.Add
            : dropped.Count > 0 ? TransactionType.Drop
            : TransactionType.Unknown;

        var parts = new List<string>();
        if (added.Count > 0)
        {
            parts.Add($"Added {string.Join(", ", added)}");
        }

        if (dropped.Count > 0)
        {
            parts.Add($"Dropped {string.Join(", ", dropped)}");
        }

        return new Transaction(
            sequence,
            timestamp,
            kind,
            franchise,
            parts.Count > 0 ? string.Join("; ", parts) : type,
            [.. added, .. dropped]);
    }

    private static IReadOnlyList<Trade> ParsePendingTrades(JsonElement root)
    {
        if (!root.TryGetProperty("pendingTrades", out var pending))
        {
            return [];
        }

        return
        [
            .. MflJson.Elements(pending, "pendingTrade").Select(e => new Trade(
                MflJson.GetString(e, "trade_id"),
                MflJson.GetString(e, "offeringteam"),
                MflJson.GetString(e, "offeredto"),
                SplitAssets(MflJson.GetString(e, "will_give_up")),
                SplitAssets(MflJson.GetString(e, "will_receive")),
                MflJson.GetString(e, "comments"),
                TradeStatus.Pending,
                DateTimeOffset.FromUnixTimeSeconds(MflJson.GetInt(e, "timestamp") ?? 0)))
        ];
    }

    private static IReadOnlyList<string> SplitAssets(string list)
        => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
