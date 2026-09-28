using Microsoft.Extensions.Logging.Abstractions;
using Swankers.League.Models;
using Swankers.League.Snapshots;
using Swankers.League.Tests.Support;
using Swankers.SnapshotCapture;

namespace Swankers.League.Tests;

public sealed class CaptureServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "swankers-tests", Path.GetRandomFileName());

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Capture_adds_the_fleecers_omits_pending_trades_and_keeps_completed_weeks()
    {
        var seed = SyntheticSnapshot.Build(week: 4);
        var reader = new FakeSnapshotReader
        {
            Franchises = [.. seed.Franchises.Where(f => !f.IsSimOnly)],
            Players = [.. seed.Players],
            Rosters = [.. seed.Rosters.Where(r => r.FranchiseId != "0099")],
            Injuries = [.. seed.Injuries],
            Matchups = [.. seed.Matchups],
            Projections = [.. seed.Projections],
            WeeklyResults = [.. seed.WeeklyResults, new WeeklyResult(4, "0001", 12m, [])], // week 4 is in progress
            Standings = [.. seed.Standings],
            Transactions = [.. seed.Transactions],
            PendingTrades = [.. seed.PendingTrades], // live offers exist but must not be captured
        };
        var store = new SnapshotStore(_root);
        var service = new CaptureService(
            reader, store,
            new CaptureOptions { Week = 4, SnapshotId = "2026-09-28" },
            NullLogger<CaptureService>.Instance);

        var id = await service.CaptureAsync(CT);
        var snapshot = await store.LoadAsync(id, CT);

        Assert.NotNull(snapshot);
        var fleecers = snapshot.Franchises.Single(f => f.Id == CaptureService.FleecersId);
        Assert.True(fleecers.IsSimOnly);
        Assert.Equal("The Fleecers", fleecers.Name);
        Assert.NotEmpty(snapshot.Rosters.Single(r => r.FranchiseId == CaptureService.FleecersId).Slots);
        Assert.Empty(snapshot.PendingTrades);
        Assert.Equal(2, snapshot.Matchups.Count);
        Assert.All(snapshot.WeeklyResults, r => Assert.Equal(3, r.Week)); // only the completed week
        Assert.Equal("mfl", snapshot.Manifest.Source);
    }

    [Fact]
    public void Fleecers_roster_uses_only_free_agents_in_the_league_roster_shape()
    {
        string[] positions = ["QB", "RB", "WR", "TE", "PK", "Def"];
        var players = new List<Player>();
        var projections = new List<Projection>();
        for (var i = 0; i < 60; i++)
        {
            players.Add(new Player($"9{i:000}", $"Synthetic, P{i}", positions[i % positions.Length], "FA"));
            projections.Add(new Projection($"9{i:000}", 4, 60 - i));
        }

        // The six highest-projected players are already rostered elsewhere.
        var rosters = new List<Roster>
        {
            new("0001", [.. players.Take(6).Select(p => new RosterSlot(p.Id, RosterStatus.Roster))]),
        };

        var first = CaptureService.BuildFleecersRoster(players, rosters, projections);
        var second = CaptureService.BuildFleecersRoster(players, rosters, projections);

        Assert.Equal(
            first.Slots.Select(s => s.PlayerId),
            second.Slots.Select(s => s.PlayerId)); // deterministic
        var taken = rosters[0].Slots.Select(s => s.PlayerId).ToHashSet();
        Assert.DoesNotContain(first.Slots, s => taken.Contains(s.PlayerId));

        var byId = players.ToDictionary(p => p.Id);
        foreach (var (position, count) in CaptureService.FleecersQuotas)
        {
            Assert.Equal(count, first.Slots.Count(s => byId[s.PlayerId].Position == position));
        }

        Assert.Equal(CaptureService.FleecersQuotas.Sum(q => q.Count), first.Slots.Count);
    }
}
