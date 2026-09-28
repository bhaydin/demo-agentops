using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Swankers.League.Models;
using Swankers.League.Sim;
using Swankers.Mcp.Security;

namespace Swankers.Mcp.Tools;

/// <summary>
/// Irreversible tools. With the gate on they return a pending confirmation that only a human
/// can approve through the web app (REST); there is no approve tool. The target is resolved
/// once, when the confirmation is created; execution acts on that target and SimLeague
/// re-validates the preconditions (player still rostered there, trade still pending).
/// </summary>
[McpServerToolType]
public sealed class IrreversibleTools(ToolRunner runner, SimLeague sim, LeagueViews views)
{
    [McpServerTool(Name = "drop_player", Destructive = true)]
    [Description("Release a player to free agency. Irreversible. When the confirmation gate is on this returns status pending_confirmation and a human must approve it in the web app before anything happens. Omit franchiseId to act for your own franchise.")]
    public Task<object> DropPlayer(
        [Description("Player id to drop")] string playerId,
        [Description("Franchise to act for; omit for your own")] string? franchiseId = null,
        CancellationToken cancellationToken = default)
        => runner.RunIrreversibleAsync(
            "drop_player",
            franchiseId,
            new Dictionary<string, object?> { ["playerId"] = playerId, ["franchiseId"] = franchiseId },
            prepare: async context =>
            {
                var (target, targetName, player) = await ResolveDropAsync(context, playerId, cancellationToken);
                return new PreparedAction(
                    target,
                    $"Drop {player.Name} ({player.Position}, {player.Team}) from {targetName} ({target})",
                    async ct =>
                    {
                        // Bound to the resolved target: SimLeague throws if the player has since moved.
                        await sim.DropPlayerAsync(target, playerId, ct);
                        return new DropResult(target, targetName, player, $"Dropped {player.Name} from {targetName}.");
                    });
            },
            cancellationToken);

    [McpServerTool(Name = "respond_to_trade", Destructive = true)]
    [Description("Accept or reject a pending trade offered to the caller's franchise. Accepting is irreversible: with the gate on it returns pending_confirmation for human approval. Rejecting executes immediately. Omit franchiseId to act for your own franchise.")]
    public Task<object> RespondToTrade(
        [Description("Trade id, e.g. \"T0001\"")] string tradeId,
        [Description("true to accept, false to reject")] bool accept,
        [Description("Franchise to act for; omit for your own")] string? franchiseId = null,
        CancellationToken cancellationToken = default)
    {
        if (!accept)
        {
            return runner.RunAsync<object>("respond_to_trade", ToolTier.Write, franchiseId, async context =>
            {
                var (trade, responder) = await ResolveTradeAsync(context, tradeId, cancellationToken);
                context.SetEffective(responder);
                var rejected = await sim.RespondToTradeAsync(trade.Id, accept: false, responder, cancellationToken);
                return await views.TradeAsync(rejected, cancellationToken);
            }, cancellationToken);
        }

        return runner.RunIrreversibleAsync(
            "respond_to_trade",
            franchiseId,
            new Dictionary<string, object?> { ["tradeId"] = tradeId, ["accept"] = accept, ["franchiseId"] = franchiseId },
            prepare: async context =>
            {
                var (trade, responder) = await ResolveTradeAsync(context, tradeId, cancellationToken);
                var view = await views.TradeAsync(trade, cancellationToken);
                return new PreparedAction(
                    responder,
                    $"Accept trade {trade.Id} from {view.FromFranchiseName}: " +
                    $"{view.ToFranchiseName} gives {Names(view.Get)} and receives {Names(view.Give)}",
                    async ct =>
                    {
                        // Bound to this trade and responder: SimLeague rejects it if no longer pending.
                        var accepted = await sim.RespondToTradeAsync(trade.Id, accept: true, responder, ct);
                        return await views.TradeAsync(accepted, ct);
                    });
            },
            cancellationToken);
    }

    private static string Names(IReadOnlyList<PlayerView> players)
        => players.Count == 0 ? "nothing" : string.Join(", ", players.Select(p => p.Name));

    /// <summary>Which franchise a drop acts for. A commissioner operation acts as whoever rosters the player.</summary>
    private async Task<(string FranchiseId, string FranchiseName, PlayerView Player)> ResolveDropAsync(
        ToolContext context, string playerId, CancellationToken ct)
    {
        var target = context.EffectiveFranchiseId;
        if (target == CallerScope.CommissionerFranchiseId)
        {
            target = (await sim.GetRostersAsync(ct))
                .FirstOrDefault(r => r.Slots.Any(s => s.PlayerId == playerId))?.FranchiseId
                ?? throw new McpException($"Player {playerId} is not on any roster.");
        }

        var names = await views.NamesAsync(ct);
        var players = await views.PlayersAsync(ct);
        return (target, LeagueViews.Name(names, target), LeagueViews.View(players, playerId));
    }

    /// <summary>Which franchise responds. A commissioner operation acts as the trade's recipient.</summary>
    private async Task<(Trade Trade, string Responder)> ResolveTradeAsync(
        ToolContext context, string tradeId, CancellationToken ct)
    {
        var trade = await sim.GetTradeAsync(tradeId, ct)
            ?? throw new McpException($"No trade '{tradeId}'.");
        var responder = context.EffectiveFranchiseId == CallerScope.CommissionerFranchiseId
            ? trade.ToFranchiseId
            : context.EffectiveFranchiseId;
        return (trade, responder);
    }
}
