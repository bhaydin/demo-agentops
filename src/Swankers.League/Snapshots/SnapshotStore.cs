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

    /// <summary>Every file a complete snapshot has. The manifest is written last.</summary>
    private static readonly string[] FileNames =
    [
        "franchises.json", "players.json", "rosters.json", "injuries.json", "matchups.json",
        "projections.json", "weekly-results.json", "standings.json", "transactions.json",
        "pending-trades.json", "manifest.json",
    ];

    private const string StagingSuffix = ".tmp";
    private const string RetiredSuffix = ".old";

    /// <summary>Ids of complete snapshots only; staging, retired, and partial directories are ignored.</summary>
    public IReadOnlyList<string> ListIds()
        => !Directory.Exists(rootDirectory)
            ? []
            : [.. Directory.EnumerateDirectories(rootDirectory)
                .Select(d => Path.GetFileName(d)!)
                .Where(id => !id.EndsWith(StagingSuffix, StringComparison.Ordinal)
                          && !id.EndsWith(RetiredSuffix, StringComparison.Ordinal)
                          && IsComplete(Path.Combine(rootDirectory, id)))
                .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Writes into a staging directory (manifest last) and publishes with a directory rename,
    /// so an interrupted save can never be discovered as a snapshot and a same-id recapture
    /// replaces the old snapshot whole rather than file by file.
    /// </summary>
    public async Task SaveAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        var final = Path.Combine(rootDirectory, snapshot.Manifest.Id);
        var staging = final + StagingSuffix;
        Directory.CreateDirectory(staging);

        await WriteAsync(staging, "franchises.json", snapshot.Franchises, cancellationToken);
        await WriteAsync(staging, "players.json", snapshot.Players, cancellationToken);
        await WriteAsync(staging, "rosters.json", snapshot.Rosters, cancellationToken);
        await WriteAsync(staging, "injuries.json", snapshot.Injuries, cancellationToken);
        await WriteAsync(staging, "matchups.json", snapshot.Matchups, cancellationToken);
        await WriteAsync(staging, "projections.json", snapshot.Projections, cancellationToken);
        await WriteAsync(staging, "weekly-results.json", snapshot.WeeklyResults, cancellationToken);
        await WriteAsync(staging, "standings.json", snapshot.Standings, cancellationToken);
        await WriteAsync(staging, "transactions.json", snapshot.Transactions, cancellationToken);
        await WriteAsync(staging, "pending-trades.json", snapshot.PendingTrades, cancellationToken);
        await WriteAsync(staging, "manifest.json", snapshot.Manifest, cancellationToken);

        Publish(staging, final);
    }

    private static void Publish(string staging, string final)
    {
        var retired = final + RetiredSuffix;
        if (Directory.Exists(retired))
        {
            Directory.Delete(retired, recursive: true);
        }

        if (Directory.Exists(final))
        {
            Directory.Move(final, retired);
        }

        Directory.Move(staging, final);

        if (Directory.Exists(retired))
        {
            Directory.Delete(retired, recursive: true);
        }
    }

    private static bool IsComplete(string directory)
        => FileNames.All(name => File.Exists(Path.Combine(directory, name)));

    public async Task<Snapshot?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(rootDirectory, id);
        if (!IsComplete(directory))
        {
            return null;
        }

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
