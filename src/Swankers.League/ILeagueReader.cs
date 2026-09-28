using Swankers.League.Models;

namespace Swankers.League;

/// <summary>
/// Read access to league state. Implemented by <c>MflExportClient</c> (live, cached, snapshot
/// fallback) and <c>SimLeague</c> (simulated). Implementations never expose owner identities:
/// franchise names come from data/franchise-names.json.
/// </summary>
public interface ILeagueReader
{
    Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken cancellationToken);

    Task<Roster> GetRosterAsync(string franchiseId, CancellationToken cancellationToken);

    /// <param name="week">Null means the current week.</param>
    Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken cancellationToken);

    Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken cancellationToken);

    Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken cancellationToken);

    /// <summary>Actual per-player results for a completed week.</summary>
    Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken cancellationToken);

    Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken cancellationToken);

    /// <param name="count">Maximum number of most-recent entries to return.</param>
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken cancellationToken);

    /// <summary>Pending trade offers involving <paramref name="franchiseId"/>.</summary>
    Task<IReadOnlyList<Trade>> GetPendingTradesAsync(string franchiseId, CancellationToken cancellationToken);
}
