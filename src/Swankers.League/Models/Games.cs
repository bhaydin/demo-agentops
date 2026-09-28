namespace Swankers.League.Models;

/// <summary>One head-to-head pairing. Scores are null for weeks not yet played.</summary>
public sealed record Matchup(
    int Week,
    string HomeFranchiseId,
    string AwayFranchiseId,
    decimal? HomeScore,
    decimal? AwayScore);

/// <summary>
/// A franchise's completed week: total score and every rostered player's actual points,
/// flagged by whether they started. Phase 5 start/sit evals score against these.
/// </summary>
public sealed record WeeklyResult(
    int Week,
    string FranchiseId,
    decimal Score,
    IReadOnlyList<PlayerResult> Players);

public sealed record PlayerResult(string PlayerId, decimal Points, bool Started);

public sealed record Standing(
    string FranchiseId,
    int Wins,
    int Losses,
    int Ties,
    decimal PointsFor,
    decimal PointsAgainst);
