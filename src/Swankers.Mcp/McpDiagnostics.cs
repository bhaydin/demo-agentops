using System.Diagnostics;

namespace Swankers.Mcp;

/// <summary>ActivitySource for this project (name per AGENTS.md); static readonly by the OTel idiom.</summary>
public static class McpDiagnostics
{
    public const string ActivitySourceName = "Swankers.Mcp";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    // Span attributes: the "intent vs action" evidence for the Friday talk.
    public const string ToolNameTag = "mcp.tool.name";
    public const string TierTag = "swankers.tool.tier";
    public const string CallerScopeTag = "swankers.caller.scope";
    public const string RequestedFranchiseTag = "swankers.franchise.requested";
    public const string EffectiveFranchiseTag = "swankers.franchise.effective";
    public const string GateDecisionTag = "swankers.gate.decision";
}
