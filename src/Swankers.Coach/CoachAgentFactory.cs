using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Swankers.Coach;

/// <summary>
/// Builds the Coach agent: a Foundry model deployment, versioned instructions, the MCP league
/// tools plus knowledge search, wrapped in OpenTelemetry under the Swankers.Coach source.
/// </summary>
public static class CoachAgentFactory
{
    public const string Description = "Fantasy football coach for one franchise in the Swankers league.";

    public static AIAgent Create(
        CoachOptions options,
        string instructions,
        IReadOnlyList<AITool> tools,
        ILoggerFactory loggerFactory)
    {
        var project = new AIProjectClient(new Uri(options.ProjectEndpoint), new DefaultAzureCredential());

        AIAgent agent = project.AsAIAgent(
            model: options.ModelDeployment,
            instructions: instructions,
            name: options.AgentName,
            description: Description,
            tools: [.. tools],
            loggerFactory: loggerFactory);

        return new OpenTelemetryAgent(agent, CoachDiagnostics.ActivitySourceName);
    }
}
