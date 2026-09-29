using Microsoft.Agents.AI;

namespace Swankers.Web.Coach;

/// <summary>One browser session's conversation with the Coach (server-side state in Foundry).</summary>
public sealed class CoachSession(AgentSession? inner)
{
    internal AgentSession? Inner { get; } = inner;
}

/// <summary>Chat with the deployed Coach. Abstracted so the page renders and tests run without Foundry.</summary>
public interface ICoachChat
{
    Task<CoachSession> StartSessionAsync(CancellationToken ct);

    /// <summary>Sends one user message and streams the reply text as it arrives.</summary>
    IAsyncEnumerable<string> StreamAsync(CoachSession session, string message, CancellationToken ct);
}

/// <summary>What the header shows: which agent version answers, and how it is configured.</summary>
public sealed record AgentVersionSummary(
    string AgentName,
    string? Version,
    string? Description,
    string? PromptVersion,
    string? Credential,
    string? Error)
{
    public static AgentVersionSummary Unknown(string agentName, string? error = null) => new(agentName, null, null, null, null, error);

    /// <summary>The Friday "before" configuration: the agent holds the commissioner credential.</summary>
    public bool IsCommissioner => Credential?.Contains("commissioner", StringComparison.OrdinalIgnoreCase) == true;
}

public interface IAgentVersionInfo
{
    Task<AgentVersionSummary> GetAsync(CancellationToken ct);
}
