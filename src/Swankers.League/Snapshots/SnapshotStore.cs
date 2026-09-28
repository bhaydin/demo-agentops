using System.Text.Json;
using System.Text.Json.Serialization;
using Swankers.League.Models;

namespace Swankers.League.Snapshots;

/// <summary>
/// Reads and writes snapshots under a root directory (data/snapshot/&lt;id&gt;/, one JSON file
/// per collection). Ids sort lexicographically (date-prefixed), so "latest" is the largest id.
/// </summary>
public sealed class SnapshotStore(string rootDirectory)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<string> ListIds()
        => !Directory.Exists(rootDirectory)
            ? []
            : [.. Directory.EnumerateDirectories(rootDirectory)
                .Select(d => Path.GetFileName(d)!)
                .Where(id => File.Exists(ManifestPath(id)))
                .Order(StringComparer.Ordinal)];

    public async Task SaveAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(rootDirectory, snapshot.Manifest.Id);
        Directory.CreateDirectory(directory);

        await WriteAsync(directory, "manifest.json", snapshot.Manifest, cancellationToken);
        await WriteAsync(directory, "franchises.json", snapshot.Franchises, cancellationToken);
        await WriteAsync(directory, "players.json", snapshot.Players, cancellationToken);
        await WriteAsync(directory, "rosters.json", snapshot.Rosters, cancellationToken);
        await WriteAsync(directory, "injuries.json", snapshot.Injuries, cancellationToken);
        await WriteAsync(directory, "matchups.json", snapshot.Matchups, cancellationToken);
        await WriteAsync(directory, "projections.json", snapshot.Projections, cancellationToken);
        await WriteAsync(directory, "weekly-results.json", snapshot.WeeklyResults, cancellationToken);
        await WriteAsync(directory, "standings.json", snapshot.Standings, cancellationToken);
        await WriteAsync(directory, "transactions.json", snapshot.Transactions, cancellationToken);
        await WriteAsync(directory, "pending-trades.json", snapshot.PendingTrades, cancellationToken);
    }

    public async Task<Snapshot?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        if (!File.Exists(ManifestPath(id)))
        {
            return null;
        }

        var directory = Path.Combine(rootDirectory, id);
        var manifest = await ReadAsync<SnapshotManifest>(directory, "manifest.json", cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        return new Snapshot(
            manifest,
            await ReadListAsync<Franchise>(directory, "franchises.json", cancellationToken),
            await ReadListAsync<Player>(directory, "players.json", cancellationToken),
            await ReadListAsync<Roster>(directory, "rosters.json", cancellationToken),
            await ReadListAsync<Injury>(directory, "injuries.json", cancellationToken),
            await ReadListAsync<Matchup>(directory, "matchups.json", cancellationToken),
            await ReadListAsync<Projection>(directory, "projections.json", cancellationToken),
            await ReadListAsync<WeeklyResult>(directory, "weekly-results.json", cancellationToken),
            await ReadListAsync<Standing>(directory, "standings.json", cancellationToken),
            await ReadListAsync<Transaction>(directory, "transactions.json", cancellationToken),
            await ReadListAsync<Trade>(directory, "pending-trades.json", cancellationToken));
    }

    public async Task<Snapshot?> LoadLatestAsync(CancellationToken cancellationToken)
        => ListIds() is [.., var latest]
            ? await LoadAsync(latest, cancellationToken)
            : null;

    private string ManifestPath(string id) => Path.Combine(rootDirectory, id, "manifest.json");

    private static async Task WriteAsync<T>(
        string directory, string fileName, T value, CancellationToken cancellationToken)
    {
        // Write-then-move keeps a reset from ever seeing a half-written file.
        var finalPath = Path.Combine(directory, fileName);
        var tempPath = finalPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    private static async Task<T?> ReadAsync<T>(
        string directory, string fileName, CancellationToken cancellationToken)
        where T : class
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(
        string directory, string fileName, CancellationToken cancellationToken)
        => await ReadAsync<List<T>>(directory, fileName, cancellationToken) ?? [];
}
