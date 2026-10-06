using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Swankers.Coach.Workplace;

/// <summary>
/// The Replay side of the workplace context boundary: a versioned, synthetic evidence fixture
/// held constant so that prompt versions can be compared. Every item is marked synthetic and its
/// reference says which fixture it came from; nothing here is, or pretends to be, a Microsoft 365
/// reference. Replay shows how the agent reasons over fixed evidence. It says nothing about
/// whether Work IQ retrieval works.
/// </summary>
public sealed class FixtureContextProvider : IWorkplaceContextProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyList<EvidenceItem> _items;

    private FixtureContextProvider(string fixture, string sha256, IReadOnlyList<EvidenceItem> items)
    {
        Fixture = fixture;
        Sha256 = sha256;
        _items = items;
    }

    public string Mode => WorkplaceModes.Replay;

    public string Fixture { get; }

    /// <summary>SHA-256 of the fixture file with line endings normalized, for run provenance.</summary>
    public string Sha256 { get; }

    public static FixtureContextProvider Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Evidence fixture not found: {path}. Set Workplace:FixturePath.", path);
        }

        // Normalized so the hash is the same on a Windows and a Linux checkout.
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        var document = JsonSerializer.Deserialize<FixtureDocument>(text, JsonOptions);
        if (document is null || string.IsNullOrWhiteSpace(document.Fixture) || document.Items is not { Count: > 0 })
        {
            throw new InvalidDataException($"{path}: an evidence fixture needs a fixture id and at least one item.");
        }

        var duplicates = document.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0 || document.Items.Any(i => string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Text)))
        {
            throw new InvalidDataException($"{path}: every item needs a unique id and text (duplicates: {string.Join(", ", duplicates)}).");
        }

        // Listed in order of receipt, as Live lists them. The received times are synthetic
        // transport metadata; each item's business date comes from its text, as in Live.
        var items = document.Items
            .OrderBy(i => i.ReceivedAt ?? DateTimeOffset.MaxValue)
            .Select(i => new EvidenceItem(
                i.Id, i.SourceType ?? "email", i.Title, i.Author, i.ReceivedAt, i.Text,
                SourceRef: $"fixture:{document.Fixture}#{i.Id}", SourceUrl: null, Synthetic: true))
            .ToList();

        return new FixtureContextProvider(document.Fixture, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), items);
    }

    public Task<IReadOnlyList<EvidenceItem>> GetEvidenceAsync(CancellationToken cancellationToken) => Task.FromResult(_items);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record FixtureDocument(string? Fixture, IReadOnlyList<FixtureItem>? Items);

    private sealed record FixtureItem(string Id, string? SourceType, string? Title, string? Author, DateTimeOffset? ReceivedAt, string Text);
}
