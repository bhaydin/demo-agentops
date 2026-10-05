using Swankers.Coach.Workplace;
using Swankers.League;

namespace Swankers.Coach.Tests;

/// <summary>
/// The Replay fixture as shipped in data/workplace: synthetic, marked as such, and in step with the
/// corpus seed text. No model and no Work IQ involved.
/// </summary>
public sealed class FixtureContextProviderTests
{
    private static readonly string Root = RepoPaths.FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("repo root not found");

    private static string FixturePath => Path.Combine(Root, "data", "workplace", "northwind-v1.evidence.json");

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Serves_the_nine_corpus_items_in_time_order()
    {
        await using var provider = FixtureContextProvider.Load(FixturePath);

        var items = await provider.GetEvidenceAsync(CT);

        Assert.Equal(WorkplaceModes.Replay, provider.Mode);
        Assert.Equal("northwind-v1", provider.Fixture);
        // Ids run in order of receipt, which is how the Live provider numbers the same messages.
        Assert.Equal(["E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9"], items.Select(i => i.Id));
        Assert.Equal(items.OrderBy(i => i.ReceivedAt).Select(i => i.Id), items.Select(i => i.Id));
        Assert.Equal("[NW-DPM] Kickoff notes", items[0].Title);   // the superseded go-live date is the oldest item
        Assert.Equal("[NW-DPM] Working session notes", items[6].Title);   // E7 is the suggestion that must not become a decision
        Assert.All(items, i => Assert.Contains("[NW-DPM]", i.Title));
    }

    [Fact]
    public async Task Chronology_is_the_stated_business_date_and_received_time_cannot_stand_in_for_it()
    {
        await using var provider = FixtureContextProvider.Load(FixturePath);

        var items = await provider.GetEvidenceAsync(CT);

        // The business dates span the fictional engagement...
        Assert.Equal(
            new DateOnly[] { new(2026, 9, 22), new(2026, 9, 29), new(2026, 9, 30), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 2), new(2026, 10, 2) },
            items.Select(i => i.BusinessDate!.Value));
        // ...while every item was "received" on one later day, exactly as in the seeded Live mailbox,
        // so an answer that leaned on received time would get the same wrong result in Replay.
        Assert.All(items, i => Assert.Equal(new DateOnly(2026, 10, 5), DateOnly.FromDateTime(i.ReceivedAt!.Value.Date)));
        Assert.All(items, i => Assert.True(i.BusinessDate < DateOnly.FromDateTime(i.ReceivedAt!.Value.Date)));
        // The go-live decision (E5) is later than the date it replaces (E1) by business date.
        Assert.True(items.Single(i => i.Id == "E5").BusinessDate > items.Single(i => i.Id == "E1").BusinessDate);
    }

    [Fact]
    public async Task Every_item_is_marked_synthetic_and_claims_no_microsoft_365_reference()
    {
        await using var provider = FixtureContextProvider.Load(FixturePath);

        var items = await provider.GetEvidenceAsync(CT);

        Assert.All(items, i =>
        {
            Assert.True(i.Synthetic);
            Assert.False(i.Synthesized);
            Assert.Equal($"fixture:northwind-v1#{i.Id}", i.SourceRef);
            Assert.Null(i.SourceUrl);
        });
    }

    [Fact]
    public async Task The_fixture_text_matches_the_corpus_seed()
    {
        var seed = File.ReadAllText(Path.Combine(Root, "data", "workplace", "corpus-seed.md"));
        await using var provider = FixtureContextProvider.Load(FixturePath);
        var items = await provider.GetEvidenceAsync(CT);

        foreach (var item in items)
        {
            Assert.Contains($"Subject: `{item.Title}`", seed);
            Assert.Contains($"({item.Id})", seed);
            Assert.All(item.Text.Split('\n'), line => Assert.Contains($"> {line}", seed));
        }

        // The seed's send order is the fixture's id order, so a mailbox seeded from it numbers the same way.
        var sendOrder = System.Text.RegularExpressions.Regex.Matches(seed, @"Subject: `([^`]+)`").Select(m => m.Groups[1].Value);
        Assert.Equal(sendOrder, items.Select(i => i.Title));
    }

    [Fact]
    public void The_hash_identifies_the_content_whatever_the_line_endings()
    {
        var text = File.ReadAllText(FixturePath).ReplaceLineEndings("\n");
        var folder = Path.Combine(Path.GetTempPath(), "swankers-coach-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var lf = Path.Combine(folder, "lf.json");
            var crlf = Path.Combine(folder, "crlf.json");
            var changed = Path.Combine(folder, "changed.json");
            File.WriteAllText(lf, text);
            File.WriteAllText(crlf, text.Replace("\n", "\r\n"));
            File.WriteAllText(changed, text.Replace("November 16", "November 23"));

            var hash = FixtureContextProvider.Load(lf).Sha256;

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash, FixtureContextProvider.Load(crlf).Sha256);
            Assert.Equal(hash, FixtureContextProvider.Load(FixturePath).Sha256);
            Assert.NotEqual(hash, FixtureContextProvider.Load(changed).Sha256);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{"fixture":"x","items":[]}""")]
    [InlineData("""{"items":[{"id":"E1","text":"t"}]}""")]
    [InlineData("""{"fixture":"x","items":[{"id":"E1","text":"a"},{"id":"E1","text":"b"}]}""")]
    [InlineData("""{"fixture":"x","items":[{"id":"E1","text":""}]}""")]
    public void A_malformed_fixture_is_rejected(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "swankers-coach-tests", Path.GetRandomFileName() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, json);

            Assert.Throws<InvalidDataException>(() => FixtureContextProvider.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_fixture_names_the_setting_to_fix()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => FixtureContextProvider.Load(Path.Combine(Root, "data", "workplace", "no-such-fixture.json")));

        Assert.Contains("Workplace:FixturePath", ex.Message);
    }
}
