namespace Swankers.League.Models;

/// <summary>One head-to-head pairing. Scores are null for weeks not yet played.</summary>
public sealed record Matchup(
    int Week,
    string HomeFranchiseId,
    string AwayFranchiseId,
    decimal? HomeScore,
    decimal? AwayScore);

public sealed record Standing(
    string FranchiseId,
    int Wins,
    int Losses,
    int Ties,
    decimal PointsFor,
    decimal PointsAgainst);
