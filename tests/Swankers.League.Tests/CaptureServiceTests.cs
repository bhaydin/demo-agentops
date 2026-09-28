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
    public async Task Capture_adds_the_fleecers_and_omits_pending_trades()
    {
        var seed = SyntheticSnapshot.Build();
        var reader = new FakeSnapshotReader
        {
            Franchises = [.. seed.Franchises.Where(f => !f.IsSimOnly)],
            Players = [.. seed.Players],
            Rosters = [.. seed.Rosters.Where(r => r.FranchiseId != "0099")],
            Injuries = [.. seed.Injuries],
            Matchups = [.. seed.Matchups],
            Projections = [.. seed.Projections],
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
        Assert.Equal("mfl", snapshot.Manifest.Source);
    }

    [Fact]
    public void Fleecers_roster_uses_only_free_agents_deterministically()
    {
        var players = new List<Player>();
        var projections = new List<Projection>();
        for (var i = 0; i < 40; i++)
        {
            var position = new[] { "QB", "RB", "WR", "TE", "PK" }[i % 5];
            players.Add(new Player($"9{i:000}", $"Synthetic, P{i}", position, "FA"));
            projections.Add(new Projection($"9{i:000}", 4, 40 - i));
        }

        // The five highest-projected players are already rostered elsewhere.
        var rosters = new List<Roster>
        {
            new("0001", [.. players.Take(5).Select(p => new RosterSlot(p.Id, RosterStatus.Roster))]),
        };

        var first = CaptureService.BuildFleecersRoster(players, rosters, projections);
        var second = CaptureService.BuildFleecersRoster(players, rosters, projections);

        Assert.Equal(15, first.Slots.Count);
        Assert.Equal(
            first.Slots.Select(s => s.PlayerId),
            second.Slots.Select(s => s.PlayerId)); // deterministic
        var taken = rosters[0].Slots.Select(s => s.PlayerId).ToHashSet();
        Assert.DoesNotContain(first.Slots, s => taken.Contains(s.PlayerId));
        // Quotas are minimums; the final best-available picks may add more at any position.
        var byId = players.ToDictionary(p => p.Id);
        Assert.True(first.Slots.Count(s => byId[s.PlayerId].Position == "QB") >= 2);
        Assert.True(first.Slots.Count(s => byId[s.PlayerId].Position == "RB") >= 4);
        Assert.True(first.Slots.Count(s => byId[s.PlayerId].Position == "WR") >= 4);
        Assert.True(first.Slots.Count(s => byId[s.PlayerId].Position == "TE") >= 2);
    }
}
