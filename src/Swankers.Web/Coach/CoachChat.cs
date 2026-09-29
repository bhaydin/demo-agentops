using System.Runtime.CompilerServices;
using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Swankers.Web.Coach;

/// <summary>
/// The deployed Coach through its Foundry agent endpoint
/// (<c>{project}/agents/{name}/endpoint/protocols/openai</c>). The endpoint's version selector
/// decides which version answers, so routing a rollback with AgentDeploy changes what this
/// chat talks to without touching the web app. Conversation state lives in Foundry sessions.
/// </summary>
public sealed class CoachChat(AIProjectClient project, IOptions<CoachEndpointOptions> options) : ICoachChat
{
    private readonly Lazy<AIAgent> _agent = new(() =>
        project.AsAIAgent(options.Value.AgentEndpoint, tools: null, clientFactory: null, services: null));

    public async Task<CoachSession> StartSessionAsync(CancellationToken ct)
        => new(await _agent.Value.CreateSessionAsync(ct));

    public async IAsyncEnumerable<string> StreamAsync(CoachSession session, string message, [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = WebDiagnostics.ActivitySource.StartActivity("web.coach.chat");
        activity?.SetTag("swankers.agent.name", options.Value.AgentName);

        await foreach (var update in _agent.Value.RunStreamingAsync(message, session.Inner, options: null, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
