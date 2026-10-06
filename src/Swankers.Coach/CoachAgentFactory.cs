using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Swankers.Coach;

/// <summary>
/// Builds the agent for whichever scenario was composed: a Foundry model deployment, versioned
/// instructions, and that scenario's tools, wrapped in OpenTelemetry under the Swankers.Coach
/// source. For the Coach the tools are the MCP league tools plus knowledge search.
/// </summary>
public static class CoachAgentFactory
{
    public const string Description = "Fantasy football coach for one franchise in the Swankers league.";

    /// <param name="description">The agent's description; the Coach's when omitted.</param>
    /// <param name="sensitiveTelemetry">
    /// Whether spans may carry message content and tool arguments and results. Null keeps the
    /// framework default (off unless OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT is true);
    /// false keeps content out even when that variable is set.
    /// </param>
    public static AIAgent Create(
        CoachOptions options,
        string instructions,
        IReadOnlyList<AITool> tools,
        ILoggerFactory loggerFactory,
        string? description = null,
        bool? sensitiveTelemetry = null)
    {
        // Hosted: the agent's managed identity. Local: skip the managed-identity probe (it only
        // logs an error and costs time on a developer machine) and cache the CLI token.
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = !FoundryEnvironment.IsHosted,
        });
        var project = new AIProjectClient(
            new Uri(options.ProjectEndpoint),
            new CachedTokenCredential(credential));

        AIAgent agent = project.AsAIAgent(
            model: options.ModelDeployment,
            instructions: instructions,
            name: options.AgentName,
            description: description ?? Description,
            tools: [.. tools],
            loggerFactory: loggerFactory);

        var traced = new OpenTelemetryAgent(agent, CoachDiagnostics.ActivitySourceName);
        if (sensitiveTelemetry is { } capture)
        {
            traced.EnableSensitiveData = capture;
        }

        return traced;
    }
}
