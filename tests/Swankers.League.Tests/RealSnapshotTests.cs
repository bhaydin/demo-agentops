using System.Text.RegularExpressions;
using Swankers.League.Models;
using Swankers.League.Sim;
using Swankers.League.Snapshots;

namespace Swankers.League.Tests;

/// <summary>
/// Checks the committed real snapshot (latest under data/snapshot/). This is the Phase 1 gate
/// evidence that a real capture parses, stays anonymized, and seeds SimLeague. Skipped when
/// no snapshot is committed.
/// </summary>
public sealed partial class RealSnapshotTests : IDisposable
{
    private readonly string _stateDir = Path.Combine(
        Path.GetTempPath(), "swankers-tests", Path.GetRandomFileName());

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_stateDir))
        {
            Directory.Delete(_stateDir, recursive: true);
        }
    }

    [Fact]
    public async Task Real_snapshot_is_internally_consistent()
    {
        var snapshot = await LoadLatestOrSkipAsync();
        var franchiseIds = snapshot.Franchises.Select(f => f.Id).ToHashSet();
        var playerIds = snapshot.Players.Select(p => p.Id).ToHashSet();

        Assert.Single(snapshot.Franchises, f => f.IsSimOnly);
        Assert.True(snapshot.Franchises.Count(f => !f.IsSimOnly) >= 2);
        Assert.All(snapshot.Rosters, r => Assert.Contains(r.FranchiseId, franchiseIds));
        Assert.All(snapshot.Rosters.SelectMany(r => r.Slots), s => Assert.Contains(s.PlayerId, playerIds));
        Assert.All(snapshot.Matchups, m =>
        {
            Assert.Contains(m.HomeFranchiseId, franchiseIds);
            Assert.Contains(m.AwayFranchiseId, franchiseIds);
        });
        Assert.All(snapshot.Transactions, t => Assert.Contains(t.FranchiseId, franchiseIds));
        Assert.All(snapshot.WeeklyResults, r => Assert.Contains(r.FranchiseId, franchiseIds));
        Assert.NotEmpty(snapshot.Projections);
        Assert.Empty(snapshot.PendingTrades);
    }

    [Fact]
    public async Task Real_snapshot_names_come_from_the_display_name_map()
    {
        var snapshot = await LoadLatestOrSkipAsync();
        var map = await FranchiseNameMap.LoadAsync(Path.Combine(RepoRoot()!, "data", "franchise-names.json"), CT);

        Assert.All(snapshot.Franchises.Where(f => !f.IsSimOnly), f => Assert.Equal(map.Resolve(f.Id), f.Name));
    }

    [Fact]
    public async Task Real_snapshot_files_contain_no_contact_details()
    {
        var snapshot = await LoadLatestOrSkipAsync();
        var directory = Path.Combine(SnapshotRoot()!, snapshot.Manifest.Id);

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var text = await File.ReadAllTextAsync(file, CT);
            Assert.DoesNotMatch(EmailPattern(), text);
            Assert.DoesNotMatch(PhonePattern(), text);
            Assert.DoesNotContain("owner", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Real_snapshot_seeds_sim_league_and_reset_restores_it()
    {
        var snapshot = await LoadLatestOrSkipAsync();
        var sim = new SimLeague(new SnapshotStore(SnapshotRoot()!), _stateDir);
        await sim.ResetAsync(snapshot.Manifest.Id, CT);
        var seeded = await File.ReadAllTextAsync(Path.Combine(_stateDir, "state.json"), CT);

        // The Friday shape: The Fleecers offer junk for a real franchise's best player.
        var target = snapshot.Rosters.First(r => r.FranchiseId != "0099" && r.Slots.Count > 0);
        var fleecers = snapshot.Rosters.Single(r => r.FranchiseId == "0099");
        var trade = await sim.ProposeTradeAsync(
            "0099", target.FranchiseId, [fleecers.Slots[0].PlayerId], [target.Slots[0].PlayerId], "gate check", CT);
        await sim.RespondToTradeAsync(trade.Id, accept: true, target.FranchiseId, CT);
        Assert.Contains((await sim.GetRosterAsync("0099", CT)).Slots, s => s.PlayerId == target.Slots[0].PlayerId);

        await sim.ResetAsync(snapshot.Manifest.Id, CT);

        Assert.Equal(seeded, await File.ReadAllTextAsync(Path.Combine(_stateDir, "state.json"), CT));
    }

    private static async Task<Snapshot> LoadLatestOrSkipAsync()
    {
        var root = SnapshotRoot();
        Assert.SkipWhen(root is null || !Directory.Exists(root), "No data/snapshot directory in this checkout.");

        var snapshot = await new SnapshotStore(root!).LoadLatestAsync(CT);
        Assert.SkipWhen(snapshot is null, "No committed snapshot yet.");
        return snapshot!;
    }

    private static string? SnapshotRoot()
        => RepoRoot() is { } root ? Path.Combine(root, "data", "snapshot") : null;

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwankersCoach.slnx")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b")]
    private static partial Regex PhonePattern();
}
