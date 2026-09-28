namespace Swankers.League.Models;

/// <summary>An NFL player as MFL lists them. <paramref name="Name"/> is "Last, First".</summary>
public sealed record Player(string Id, string Name, string Position, string Team);

/// <summary>
/// Injury-report entry. <paramref name="Status"/> is MFL's raw text (Questionable, Out, IR, ...)
/// and <paramref name="ExpectedReturn"/> is free text ("Sep 13, 2026"); neither is normalized.
/// </summary>
public sealed record Injury(string PlayerId, string Status, string Details, string? ExpectedReturn);

/// <summary>Projected fantasy points for one player in one week, using league scoring.</summary>
public sealed record Projection(string PlayerId, int Week, decimal Points);
