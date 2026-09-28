using Swankers.League.Models;

namespace Swankers.League;

/// <summary>
/// Write access to league state. Implemented ONLY by <c>SimLeague</c> (AGENTS.md hard rule 2:
/// all writes go to the simulated league). No implementation may ever target a real fantasy
/// platform, and <c>MflExportClient</c> must never implement this interface.
/// </summary>
public interface ILeagueWriter
{
    Task<Lineup> SetLineupAsync(
        string franchiseId,
        int week,
        IReadOnlyList<string> starterPlayerIds,
        CancellationToken cancellationToken);

    Task<Trade> ProposeTradeAsync(
        string fromFranchiseId,
        string toFranchiseId,
        IReadOnlyList<string> give,
        IReadOnlyList<string> get,
        string note,
        CancellationToken cancellationToken);

    Task<Trade> RespondToTradeAsync(
        string tradeId,
        bool accept,
        string respondingFranchiseId,
        CancellationToken cancellationToken);

    Task DropPlayerAsync(string franchiseId, string playerId, CancellationToken cancellationToken);
}
