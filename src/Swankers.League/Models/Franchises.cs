namespace Swankers.League.Models;

/// <summary>
/// A league franchise. <paramref name="Name"/> is always the display name from
/// data/franchise-names.json — never a real owner name. <paramref name="IsSimOnly"/> marks
/// franchises that exist only in SimLeague (The Fleecers).
/// </summary>
public sealed record Franchise(string Id, string Name, bool IsSimOnly = false);

/// <summary>Where a rostered player sits.</summary>
public enum RosterStatus
{
    Unknown = 0,
    Roster,
    TaxiSquad,
    InjuredReserve,
}

public sealed record RosterSlot(string PlayerId, RosterStatus Status);

public sealed record Roster(string FranchiseId, IReadOnlyList<RosterSlot> Slots);

/// <summary>The starters a franchise has declared for a week.</summary>
public sealed record Lineup(string FranchiseId, int Week, IReadOnlyList<string> StarterPlayerIds);
