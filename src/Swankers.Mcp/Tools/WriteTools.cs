using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Swankers.League.Sim;
using Swankers.Mcp.Security;

namespace Swankers.Mcp.Tools;

/// <summary>Write-tier tools: reversible changes to the simulated league. Scope-checked, not gated.</summary>
[McpServerToolType]
public sealed class WriteTools(ToolRunner runner, SimLeague sim, LeagueViews views)
{
    [McpServerTool(Name = "set_lineup", Idempotent = true)]
    [Description("Declare the starters for the current week. Every starter must be on the franchise's roster. Omit franchiseId to act for your own franchise.")]
    public Task<LineupResult> SetLineup(
        [Description("Player ids to start")] string[] starters,
        [Description("Franchise to act for; omit for your own")] string? franchiseId = null,
        CancellationToken cancellationToken = default)
        => runner.RunAsync("set_lineup", ToolTier.Write, franchiseId, async context =>
        {
            var target = RequireNamedFranchise(context);
            var week = await sim.GetCurrentWeekAsync(cancellationToken);
            var lineup = await sim.SetLineupAsync(target, week, starters, cancellationToken);

            var names = await views.NamesAsync(cancellationToken);
            var players = await views.PlayersAsync(cancellationToken);
            return new LineupResult(
                target, LeagueViews.Name(names, target), week,
                [.. lineup.StarterPlayerIds.Select(id => LeagueViews.View(players, id))]);
        }, cancellationToken);

    [McpServerTool(Name = "propose_trade")]
    [Description("Offer a trade to another franchise. 'give' are your player ids, 'get' are theirs. The note is shown to the other owner. Omit franchiseId to act for your own franchise.")]
    public Task<TradeView> ProposeTrade(
        [Description("Franchise id receiving the offer")] string toFranchiseId,
        [Description("Player ids you give")] string[] give,
        [Description("Player ids you receive")] string[] get,
        [Description("Message to the other owner")] string note,
        [Description("Franchise to act for; omit for your own")] string? franchiseId = null,
        CancellationToken cancellationToken = default)
        => runner.RunAsync("propose_trade", ToolTier.Write, franchiseId, async context =>
        {
            var from = RequireNamedFranchise(context);
            var trade = await sim.ProposeTradeAsync(from, toFranchiseId, give, get, note, cancellationToken);
            return await views.TradeAsync(trade, cancellationToken);
        }, cancellationToken);

    /// <summary>A commissioner operation ("0000") has no roster of its own; these tools need a real target.</summary>
    internal static string RequireNamedFranchise(ToolContext context)
        => context.EffectiveFranchiseId == CallerScope.CommissionerFranchiseId
            ? throw new McpException("A commissioner operation (franchiseId 0000) must name the franchise to act for.")
            : context.EffectiveFranchiseId;
}
