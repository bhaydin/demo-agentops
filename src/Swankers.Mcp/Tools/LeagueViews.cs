using Swankers.League.Models;
using Swankers.League.Sim;
using Swankers.Mcp.Gate;

namespace Swankers.Mcp.Tools;

public sealed record PlayerView(string Id, string Name, string Position, string Team, string? RosterStatus);

public sealed record RosterView(string FranchiseId, string FranchiseName, IReadOnlyList<PlayerView> Players);

public sealed record InjuryView(string Status, string Details, string? ExpectedReturn);

public sealed record PlayerNewsView(PlayerView Player, InjuryView? Injury, string Note);

/// <summary><paramref name="Source"/> is mfl-live, mfl-cache, or "snapshot &lt;id&gt;"; <paramref name="AsOf"/> is when that data was fetched or captured.</summary>
public sealed record NewsResult(string Query, string Source, DateTimeOffset? AsOf, IReadOnlyList<PlayerNewsView> Players);

public sealed record ProjectedPlayerView(string Id, string Name, string Position, decimal ProjectedPoints);

public sealed record MatchupView(
    int Week,
    string FranchiseId,
    string FranchiseName,
    string OpponentId,
    string OpponentName,
    decimal? FranchiseScore,
    decimal? OpponentScore,
    IReadOnlyList<ProjectedPlayerView> Projections);

public sealed record StandingView(
    int Rank, string FranchiseId, string FranchiseName, int Wins, int Losses, int Ties, decimal PointsFor, decimal PointsAgainst);

public sealed record TradeView(
    string Id,
    string FromFranchiseId,
    string FromFranchiseName,
    string ToFranchiseId,
    string ToFranchiseName,
    IReadOnlyList<PlayerView> Give,
    IReadOnlyList<PlayerView> Get,
    string Note,
    string Status,
    DateTimeOffset OfferedOn);

public sealed record LineupResult(string FranchiseId, string FranchiseName, int Week, IReadOnlyList<PlayerView> Starters);

public sealed record DropResult(string FranchiseId, string FranchiseName, PlayerView Player, string Message);

public sealed record FranchiseStateView(string Id, string Name, bool IsSimOnly, IReadOnlyList<PlayerView> Roster);

public sealed record TransactionView(
    long Sequence, DateTimeOffset TimestampUtc, string Type, string FranchiseId, string FranchiseName, string Description, IReadOnlyList<string> PlayerNames);

public sealed record LeagueStateView(
    int Week,
    string OwnerFranchiseId,
    IReadOnlyList<FranchiseStateView> Franchises,
    IReadOnlyList<TransactionView> Transactions,
    IReadOnlyList<TradeView> PendingTrades,
    IReadOnlyList<PendingConfirmation> PendingConfirmations,
    IReadOnlyList<ResolvedConfirmation> RecentConfirmations);

/// <summary>Builds tool and ticker views over SimLeague state with names resolved.</summary>
public sealed class LeagueViews(SimLeague sim)
{
    public async Task<IReadOnlyDictionary<string, Player>> PlayersAsync(CancellationToken ct)
        => (await sim.GetPlayersAsync(ct)).ToDictionary(p => p.Id, StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(CancellationToken ct)
        => (await sim.GetFranchisesAsync(ct)).ToDictionary(f => f.Id, f => f.Name, StringComparer.Ordinal);

    public static string Name(IReadOnlyDictionary<string, string> names, string franchiseId)
        => names.TryGetValue(franchiseId, out var name) ? name
            : franchiseId == "0000" ? "Commissioner"
            : $"Franchise {franchiseId}";

    public static PlayerView View(IReadOnlyDictionary<string, Player> players, string playerId, RosterStatus? status = null)
        => players.TryGetValue(playerId, out var p)
            ? new PlayerView(p.Id, p.Name, p.Position, p.Team, status?.ToString())
            : new PlayerView(playerId, "Unknown player", "?", "?", status?.ToString());

    public async Task<RosterView> RosterAsync(string franchiseId, CancellationToken ct)
    {
        var names = await NamesAsync(ct);
        if (!names.ContainsKey(franchiseId))
        {
            throw new KeyNotFoundException($"No franchise '{franchiseId}' in the league.");
        }

        var players = await PlayersAsync(ct);
        var roster = await sim.GetRosterAsync(franchiseId, ct);
        return new RosterView(
            franchiseId,
            Name(names, franchiseId),
            [.. roster.Slots.Select(s => View(players, s.PlayerId, s.Status))]);
    }

    public async Task<TradeView> TradeAsync(Trade trade, CancellationToken ct)
    {
        var names = await NamesAsync(ct);
        var players = await PlayersAsync(ct);
        return Trade(trade, names, players);
    }

    public static TradeView Trade(Trade t, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, Player> players)
        => new(
            t.Id,
            t.FromFranchiseId, Name(names, t.FromFranchiseId),
            t.ToFranchiseId, Name(names, t.ToFranchiseId),
            [.. t.Give.Select(id => View(players, id))],
            [.. t.Get.Select(id => View(players, id))],
            t.Note,
            t.Status.ToString(),
            t.OfferedOn);

    public async Task<LeagueStateView> StateAsync(string ownerFranchiseId, ConfirmationGate gate, CancellationToken ct)
    {
        var names = await NamesAsync(ct);
        var players = await PlayersAsync(ct);
        var franchises = await sim.GetFranchisesAsync(ct);
        var rosters = (await sim.GetRostersAsync(ct)).ToDictionary(r => r.FranchiseId);

        var pendingTrades = new List<TradeView>();
        foreach (var franchise in franchises)
        {
            pendingTrades.AddRange((await sim.GetPendingTradesAsync(franchise.Id, ct))
                .Where(t => t.FromFranchiseId == franchise.Id) // once per trade
                .Select(t => Trade(t, names, players)));
        }

        return new LeagueStateView(
            await sim.GetCurrentWeekAsync(ct),
            ownerFranchiseId,
            [.. franchises.Select(f => new FranchiseStateView(
                f.Id, f.Name, f.IsSimOnly,
                rosters.TryGetValue(f.Id, out var r)
                    ? [.. r.Slots.Select(s => View(players, s.PlayerId, s.Status))]
                    : []))],
            [.. (await sim.GetTransactionsAsync(50, ct)).Select(t => new TransactionView(
                t.Sequence, t.TimestampUtc, t.Type.ToString(), t.FranchiseId, Name(names, t.FranchiseId),
                t.Description, [.. t.PlayerIds.Select(id => View(players, id).Name)]))],
            pendingTrades,
            gate.Pending,
            gate.Recent);
    }
}
