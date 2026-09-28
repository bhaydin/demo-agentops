namespace Swankers.SnapshotCapture;

public sealed class CaptureOptions
{
    public const string SectionName = "Capture";

    /// <summary>Current NFL week; matchups are captured for weeks 1..Week.</summary>
    public int Week { get; set; } = 1;

    /// <summary>Snapshot root, relative to the working directory (repo root).</summary>
    public string OutputRoot { get; set; } = Path.Combine("data", "snapshot");

    /// <summary>Maintainer-controlled display names; required for anonymization.</summary>
    public string FranchiseNamesPath { get; set; } = Path.Combine("data", "franchise-names.json");

    /// <summary>How many recent league transactions to keep in the snapshot.</summary>
    public int TransactionCount { get; set; } = 30;

    /// <summary>Snapshot id; defaults to today (UTC) so ids sort chronologically.</summary>
    public string? SnapshotId { get; set; }
}
