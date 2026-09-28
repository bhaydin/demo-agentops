using Swankers.League.Models;
using Swankers.League.Tests.Support;

namespace Swankers.League.Tests;

/// <summary>
/// Parsers against MFL's JSON quirks: every value a string, empty collections as {},
/// and single-item collections as an object instead of a one-element array.
/// </summary>
public class MflParsingTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Players_and_injuries_parse_from_verified_shapes()
    {
        const string json = """
            {
              "players": { "timestamp": "1790546401", "player": [
                { "id": "11225", "name": "Synthetic, Cee", "position": "RB", "team": "FA" },
                { "id": "22334", "name": "Fake, Player", "position": "QB", "team": "GBP" } ] },
              "injuries": { "week": "4", "injury": [
                { "id": "11317", "status": "Questionable", "details": "Hamstring", "exp_return": "Sep 13, 2026" } ] }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var players = await client.GetPlayersAsync(CT);
        var injuries = await client.GetInjuriesAsync(4, CT);

        Assert.Equal(2, players.Count);
        Assert.Equal("Synthetic, Cee", players[0].Name);
        var injury = Assert.Single(injuries);
        Assert.Equal("Questionable", injury.Status);
        Assert.Equal("Sep 13, 2026", injury.ExpectedReturn);
    }

    [Fact]
    public async Task Empty_collection_returned_as_empty_object_parses_to_empty_list()
    {
        // Verified live: TYPE=pendingTrades with no offers returns "pendingTrades": {}.
        var (client, _, _) = MflTest.Create(
            responder: _ => RecordingHandler.Json("""{ "pendingTrades": {} }"""));

        var trades = await client.GetPendingTradesAsync("0001", CT);

        Assert.Empty(trades);
    }

    [Fact]
    public async Task Single_item_collection_returned_as_object_parses_as_one_item()
    {
        const string json = """
            {
              "rosters": { "franchise": {
                "id": "0001",
                "player": { "id": "11225", "status": "ROSTER" } } }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var roster = await client.GetRosterAsync("0001", CT);

        var slot = Assert.Single(roster.Slots);
        Assert.Equal("11225", slot.PlayerId);
        Assert.Equal(RosterStatus.Roster, slot.Status);
    }

    [Fact]
    public async Task Standings_parse_numeric_strings()
    {
        const string json = """
            {
              "leagueStandings": { "franchise": [
                { "id": "0001", "h2hw": "3", "h2hl": "1", "h2ht": "0", "pf": "512.42", "pa": "455.1" } ] }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var standing = Assert.Single(await client.GetStandingsAsync(CT));

        Assert.Equal(3, standing.Wins);
        Assert.Equal(512.42m, standing.PointsFor);
    }

    [Fact]
    public async Task Matchups_prefer_the_isHome_flag()
    {
        const string json = """
            {
              "schedule": { "weeklySchedule": [
                { "week": "3", "matchup": [
                  { "franchise": [
                    { "id": "0002", "score": "101.5", "isHome": "0" },
                    { "id": "0001", "score": "98.2", "isHome": "1" } ] } ] } ] }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var matchup = Assert.Single(await client.GetMatchupsAsync(3, CT));

        Assert.Equal("0001", matchup.HomeFranchiseId);
        Assert.Equal("0002", matchup.AwayFranchiseId);
        Assert.Equal(98.2m, matchup.HomeScore);
    }

    [Fact]
    public async Task Transactions_split_added_and_dropped_and_skip_system_entries()
    {
        // Format verified against the live league: "added,ids,|dropped,ids,", newest first.
        const string json = """
            {
              "transactions": { "transaction": [
                { "type": "FREE_AGENT", "franchise": "0008", "transaction": "16432,|16597,", "timestamp": "1790510000" },
                { "type": "LOCK_ALL_PLAYERS", "franchise": "", "transaction": "", "timestamp": "1790500000" },
                { "type": "WAIVER", "franchise": "0009", "transaction": "17474,|", "timestamp": "1790400000" } ] }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var transactions = await client.GetTransactionsAsync(30, CT);

        Assert.Equal(2, transactions.Count);                 // system entry skipped
        Assert.Equal("0009", transactions[0].FranchiseId);   // oldest first
        Assert.Equal([1L, 2L], transactions.Select(t => t.Sequence));
        var addDrop = transactions[1];
        Assert.Equal(TransactionType.Add, addDrop.Type);
        Assert.Equal(["16432", "16597"], addDrop.PlayerIds);
        Assert.Equal("Added 16432; Dropped 16597", addDrop.Description);
    }

    [Fact]
    public async Task Weekly_results_parse_matchup_franchises_and_player_rows()
    {
        const string json = """
            {
              "weeklyResults": { "week": "2", "matchup": [
                { "franchise": [
                  { "id": "0001", "score": "84.3", "player": [
                    { "id": "13589", "score": "23.8", "status": "starter" },
                    { "id": "14000", "score": "11.0", "status": "nonstarter" } ] },
                  { "id": "0004", "score": "123.0", "player": { "id": "15000", "score": "30.5", "status": "starter" } } ] } ] }
            }
            """;
        var (client, _, _) = MflTest.Create(responder: _ => RecordingHandler.Json(json));

        var results = await client.GetWeeklyResultsAsync(2, CT);

        Assert.Equal(2, results.Count);
        var first = results.Single(r => r.FranchiseId == "0001");
        Assert.Equal(84.3m, first.Score);
        Assert.Contains(first.Players, p => p is { PlayerId: "13589", Points: 23.8m, Started: true });
        Assert.Contains(first.Players, p => p is { PlayerId: "14000", Started: false });
        Assert.Single(results.Single(r => r.FranchiseId == "0004").Players); // single object, not array
    }

    [Fact]
    public async Task Unexpected_shapes_yield_empty_results_not_exceptions()
    {
        var (client, _, _) = MflTest.Create(
            responder: _ => RecordingHandler.Json("""{ "unexpected": "shape" }"""));

        Assert.Empty(await client.GetFranchisesAsync(CT));
        Assert.Empty(await client.GetStandingsAsync(CT));
        Assert.Empty(await client.GetTransactionsAsync(10, CT));
        Assert.Empty(await client.GetWeeklyResultsAsync(1, CT));
    }
}
