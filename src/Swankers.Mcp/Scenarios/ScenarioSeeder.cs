using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Swankers.League.Models;
using Swankers.League.Sim;
using Swankers.Mcp.Tools;

namespace Swankers.Mcp.Scenarios;

/// <summary>How a scenario picks players: explicit ids, or a rule over a roster.</summary>
public sealed record AssetRule(string Rule, string? Position, int Count, IReadOnlyList<string>? PlayerIds);

/// <summary>A trade-offer scenario from data/demo. Rules resolve at seed time so re-captures don't break it.</summary>
public sealed record TradeScenario(
    string Id, string FromFranchiseId, string ToFranchiseId, AssetRule Give, AssetRule Get, string Note);

public sealed record SeedResult(string Scenario, TradeView Trade);

/// <summary>
/// Loads data/demo/&lt;scenario&gt;.json and applies it to SimLeague. Scenarios only ever create
/// simulated-league state (a trade offer); the note text is stored verbatim as untrusted data.
/// </summary>
public sealed partial class ScenarioSeeder(SimLeague sim, LeagueViews views, IOptions<McpOptions> options, ILogger<ScenarioSeeder> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SeedResult> SeedAsync(string scenarioId, CancellationToken cancellationToken)
    {
        if (!ScenarioIdPattern().IsMatch(scenarioId))
        {
            throw new ArgumentException("Scenario ids are lowercase letters, digits, and dashes.");
        }

        var path = Path.Combine(options.Value.ScenarioRoot, scenarioId + ".json");
        if (!File.Exists(path))
        {
            throw new KeyNotFoundException($"No scenario '{scenarioId}'.");
        }

        await using var stream = File.OpenRead(path);
        var scenario = await JsonSerializer.DeserializeAsync<TradeScenario>(stream, Json, cancellationToken)
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' is empty.");

        var week = await sim.GetCurrentWeekAsync(cancellationToken);
        var projections = (await sim.GetProjectionsAsync(week, cancellationToken))
            .ToDictionary(p => p.PlayerId, p => p.Points, StringComparer.Ordinal);
        var players = await views.PlayersAsync(cancellationToken);

        var give = Resolve(scenario.Give, await sim.GetRosterAsync(scenario.FromFranchiseId, cancellationToken), projections, players);
        var get = Resolve(scenario.Get, await sim.GetRosterAsync(scenario.ToFranchiseId, cancellationToken), projections, players);

        var trade = await sim.ProposeTradeAsync(
            scenario.FromFranchiseId, scenario.ToFranchiseId, give, get, scenario.Note, cancellationToken);
        logger.LogInformation("Seeded scenario {Scenario} as trade {TradeId}.", scenarioId, trade.Id);
        return new SeedResult(scenarioId, await views.TradeAsync(trade, cancellationToken));
    }

    internal static IReadOnlyList<string> Resolve(
        AssetRule rule,
        Roster roster,
        IReadOnlyDictionary<string, decimal> projections,
        IReadOnlyDictionary<string, Player> players)
    {
        if (rule.PlayerIds is { Count: > 0 })
        {
            return rule.PlayerIds;
        }

        var candidates = roster.Slots
            .Select(s => s.PlayerId)
            .Where(id => rule.Position is null ||
                         (players.TryGetValue(id, out var p) && p.Position.Equals(rule.Position, StringComparison.OrdinalIgnoreCase)));

        var ordered = rule.Rule switch
        {
            "highest_projected" => candidates.OrderByDescending(id => projections.GetValueOrDefault(id)).ThenBy(id => id, StringComparer.Ordinal),
            "lowest_projected" => candidates.OrderBy(id => projections.GetValueOrDefault(id)).ThenBy(id => id, StringComparer.Ordinal),
            _ => throw new InvalidOperationException($"Unknown asset rule '{rule.Rule}'."),
        };

        var picked = ordered.Take(Math.Max(1, rule.Count)).ToList();
        return picked.Count > 0
            ? picked
            : throw new InvalidOperationException(
                $"No players on franchise {roster.FranchiseId} match rule {rule.Rule}{(rule.Position is null ? "" : $" ({rule.Position})")}.");
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex ScenarioIdPattern();
}
