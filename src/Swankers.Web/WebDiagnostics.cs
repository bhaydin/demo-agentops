using System.Diagnostics;

namespace Swankers.Web;

/// <summary>ActivitySource for the web app (name fixed by AGENTS.md). Trace context propagates to MCP and Foundry over HTTP.</summary>
public static class WebDiagnostics
{
    public const string ActivitySourceName = "Swankers.Web";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
