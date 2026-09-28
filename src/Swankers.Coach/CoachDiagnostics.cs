using System.Diagnostics;

namespace Swankers.Coach;

/// <summary>ActivitySource for this project (name per AGENTS.md). MAF agent and tool spans are emitted under it.</summary>
public static class CoachDiagnostics
{
    public const string ActivitySourceName = "Swankers.Coach";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
