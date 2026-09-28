using System.Diagnostics;

namespace Swankers.League;

/// <summary>
/// Shared ActivitySource for this project (name per AGENTS.md). Static readonly by the standard
/// OpenTelemetry idiom; it holds no mutable state.
/// </summary>
public static class LeagueDiagnostics
{
    public const string ActivitySourceName = "Swankers.League";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
