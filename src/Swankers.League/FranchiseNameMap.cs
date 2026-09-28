using System.Text.Json;

namespace Swankers.League;

/// <summary>
/// Maps franchise ids to the maintainer-controlled display names in data/franchise-names.json.
/// Every reader resolves names through this map, so MFL-side names (which can hint at real
/// owners) never reach callers. Unmapped ids get a neutral placeholder.
/// </summary>
public sealed class FranchiseNameMap
{
    private readonly IReadOnlyDictionary<string, string> _names;

    public FranchiseNameMap(IReadOnlyDictionary<string, string> names)
        => _names = names.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    public static FranchiseNameMap Empty { get; } = new(new Dictionary<string, string>());

    public string Resolve(string franchiseId)
        => _names.TryGetValue(franchiseId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Franchise {franchiseId}";

    /// <summary>Loads the { "franchises": { "0001": "Name", ... } } shape of franchise-names.json.</summary>
    public static async Task<FranchiseNameMap> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("franchises", out var franchises) &&
            franchises.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in franchises.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    names[property.Name] = property.Value.GetString() ?? "";
                }
            }
        }

        return new FranchiseNameMap(names);
    }
}
