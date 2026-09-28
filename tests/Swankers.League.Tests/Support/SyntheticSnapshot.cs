using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Tests.Support;

internal static class SyntheticSnapshot
{
    /// <summary>
    /// A small, fully populated snapshot with synthetic data only. Week 3 is completed
    /// (scored matchup + weekly results); <paramref name="week"/> is upcoming (unscored).
    /// </summary>
    public static Snapshot Build(string id = "2026-09-27", int week = 4) => new(
        new SnapshotManifest(id, new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), week, "synthetic"),
        [new Franchise("0001", "The Swank"), new Franchise("0002", "Mock Dynasty"), new Franchise("0099", "The Fleecers", IsSimOnly: true)],
        [
            new Player("1001", "Synthetic, Quin", "QB", "GBP"),
            new Player("1002", "Fake, Runner", "RB", "CHI"),
            new Player("1003", "Test, Wide", "WR", "FA"),
            new Player("1004", "Sample, Tight", "TE", "DET"),
        ],
        [
            new Roster("0001", [new RosterSlot("1001", RosterStatus.Roster), new RosterSlot("1002", RosterStatus.Roster)]),
            new Roster("0002", [new RosterSlot("1004", RosterStatus.InjuredReserve)]),
            new Roster("0099", [new RosterSlot("1003", RosterStatus.Roster)]),
        ],
        [new Injury("1004", "Questionable", "Hamstring", "Oct 4, 2026")],
        [
            new Matchup(week - 1, "0002", "0001", 98.2m, 101.5m),
            new Matchup(week, "0001", "0002", null, null),
        ],
        [new Projection("1001", week, 18.4m), new Projection("1002", week, 11.2m)],
        [
            new WeeklyResult(week - 1, "0001", 101.5m, [new PlayerResult("1001", 24.1m, true), new PlayerResult("1002", 3.2m, false)]),
            new WeeklyResult(week - 1, "0002", 98.2m, [new PlayerResult("1004", 0m, true)]),
        ],
        [new Standing("0001", 3, 1, 0, 512.4m, 455.1m), new Standing("0002", 1, 3, 0, 401.0m, 480.2m)],
        [new Transaction(1, new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero), TransactionType.Add, "0002", "Added 1003", ["1003"])],
        [new Trade("t-1", "0099", "0001", ["1003"], ["1002"], "Fair offer, promise!", TradeStatus.Pending, new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero))]);
}
