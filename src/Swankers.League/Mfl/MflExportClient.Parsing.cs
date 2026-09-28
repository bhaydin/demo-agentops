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

    private static IReadOnlyList<Transaction> ParseTransactions(JsonElement root)
    {
        if (!root.TryGetProperty("transactions", out var transactions))
        {
            return [];
        }

        var sequence = 0L;
        return
        [
            .. MflJson.Elements(transactions, "transaction").Select(e => new Transaction(
                ++sequence,
                DateTimeOffset.FromUnixTimeSeconds(MflJson.GetInt(e, "timestamp") ?? 0),
                ParseTransactionType(MflJson.GetString(e, "type")),
                MflJson.GetString(e, "franchise"),
                MflJson.GetString(e, "transaction"),
                []))
        ];
    }

    private static TransactionType ParseTransactionType(string type)
    {
        var upper = type.ToUpperInvariant();
        return upper switch
        {
            _ when upper.Contains("TRADE") => TransactionType.TradeAccepted,
            _ when upper.Contains("DROP") => TransactionType.Drop,
            _ when upper.Contains("WAIVER") || upper.Contains("FREE_AGENT") => TransactionType.Add,
            _ => TransactionType.Unknown,
        };
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
