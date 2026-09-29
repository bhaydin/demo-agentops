using System.Runtime.CompilerServices;
using Swankers.Web.Coach;

namespace Swankers.Web.Tests.Support;

/// <summary>A Coach that answers from a script, and a routed-version lookup with a fixed answer.</summary>
public sealed class FakeCoach : ICoachChat, IAgentVersionInfo
{
    public AgentVersionSummary Version { get; set; } =
        new("Coach", "7", "Coach v1-owner: prompt v1, owner credential, model gpt-5.4", "v1", "owner", null, "gpt-5.4");

    public List<string> Received { get; } = [];

    public Func<string, IEnumerable<string>> Reply { get; set; } = _ => ["Start ", "Judkins."];

    public int SessionsStarted { get; private set; }

    public Task<CoachSession> StartSessionAsync(CancellationToken ct)
    {
        SessionsStarted++;
        return Task.FromResult(new CoachSession(null));
    }

    public async IAsyncEnumerable<string> StreamAsync(CoachSession session, string message, [EnumeratorCancellation] CancellationToken ct)
    {
        Received.Add(message);
        foreach (var chunk in Reply(message))
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    public Task<AgentVersionSummary> GetAsync(CancellationToken ct) => Task.FromResult(Version);
}
