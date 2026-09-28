namespace Swankers.League.Mfl;

public enum DataSource
{
    /// <summary>Fetched from MFL for this call.</summary>
    Live,

    /// <summary>Served from this process's cache of an earlier live fetch.</summary>
    Cache,

    /// <summary>Served from the latest snapshot because MFL was unavailable.</summary>
    Snapshot,
}

/// <summary>
/// A value together with where it came from and how fresh it is, so callers can tell live
/// data from fallback data instead of inferring it from configuration.
/// </summary>
public sealed record Sourced<T>(T Value, DataSource Source, DateTimeOffset AsOf, string? SnapshotId = null);
