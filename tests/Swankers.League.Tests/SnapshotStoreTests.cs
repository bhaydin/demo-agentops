using System.Text.Json;
using Swankers.League.Snapshots;
using Swankers.League.Tests.Support;

namespace Swankers.League.Tests;

public sealed class SnapshotStoreTests : IDisposable
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
    public async Task Snapshot_round_trips_exactly()
    {
        var store = new SnapshotStore(_root);
        var original = SyntheticSnapshot.Build();

        await store.SaveAsync(original, CT);
        var loaded = await store.LoadAsync(original.Manifest.Id, CT);

        Assert.NotNull(loaded);
        Assert.Equal(
            JsonSerializer.Serialize(original, SnapshotStore.JsonOptions),
            JsonSerializer.Serialize(loaded, SnapshotStore.JsonOptions));
    }

    [Fact]
    public async Task Latest_snapshot_is_the_largest_id()
    {
        var store = new SnapshotStore(_root);
        await store.SaveAsync(SyntheticSnapshot.Build("2026-09-20", week: 3), CT);
        await store.SaveAsync(SyntheticSnapshot.Build("2026-09-27", week: 4), CT);

        var latest = await store.LoadLatestAsync(CT);

        Assert.Equal("2026-09-27", latest!.Manifest.Id);
        Assert.Equal(["2026-09-20", "2026-09-27"], store.ListIds());
    }

    [Fact]
    public async Task Empty_store_yields_null_latest_and_empty_reader_results()
    {
        var store = new SnapshotStore(_root);
        var reader = new SnapshotLeagueReader(store);

        Assert.Null(await store.LoadLatestAsync(CT));
        Assert.Empty(await reader.GetFranchisesAsync(CT));
        Assert.Empty(await reader.GetPendingTradesAsync("0001", CT));
    }

    [Fact]
    public async Task Snapshot_reader_serves_week_scoped_views()
    {
        var store = new SnapshotStore(_root);
        await store.SaveAsync(SyntheticSnapshot.Build(week: 4), CT);
        var reader = new SnapshotLeagueReader(store);

        Assert.Single(await reader.GetMatchupsAsync(4, CT));
        Assert.Empty(await reader.GetMatchupsAsync(5, CT));
        Assert.Equal(2, (await reader.GetProjectionsAsync(4, CT)).Count);
        Assert.Single((await reader.GetRosterAsync("0099", CT)).Slots);
        Assert.Single(await reader.GetPendingTradesAsync("0099", CT));
    }

    [Fact]
    public async Task Snapshot_files_contain_only_allow_listed_properties()
    {
        // The privacy guarantee is structural: only domain records are serialized. This test
        // locks the serialized surface so a new property (e.g. an owner field) fails loudly
        // until it is deliberately added here.
        string[] allowed =
        [
            "id", "capturedAtUtc", "week", "source",                          // manifest
            "name", "isSimOnly",                                              // franchise
            "position", "team",                                               // player
            "franchiseId", "slots", "playerId", "status",                     // roster
            "details", "expectedReturn",                                      // injury
            "homeFranchiseId", "awayFranchiseId", "homeScore", "awayScore",   // matchup
            "points",                                                         // projection
            "wins", "losses", "ties", "pointsFor", "pointsAgainst",           // standing
            "sequence", "timestampUtc", "type", "description", "playerIds",   // transaction
            "fromFranchiseId", "toFranchiseId", "give", "get", "note", "offeredOn", // trade
        ];

        var store = new SnapshotStore(_root);
        await store.SaveAsync(SyntheticSnapshot.Build(), CT);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(_root, "2026-09-27"), "*.json"))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, CT));
            foreach (var property in EnumeratePropertyNames(doc.RootElement))
            {
                Assert.Contains(property, allowed);
            }
        }
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in EnumeratePropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumeratePropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
