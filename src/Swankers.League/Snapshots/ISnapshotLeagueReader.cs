namespace Swankers.League.Snapshots;

/// <summary>
/// A reader backed by the latest snapshot in data/snapshot/. MflExportClient uses it as the
/// offline fallback when MFL is unreachable or throttling; the distinct interface keeps DI from
/// handing the live client its own instance as a fallback.
/// </summary>
public interface ISnapshotLeagueReader : ILeagueReader
{
    /// <summary>The manifest of the snapshot being served, or null when the store is empty.</summary>
    Task<SnapshotManifest?> GetManifestAsync(CancellationToken cancellationToken);
}
