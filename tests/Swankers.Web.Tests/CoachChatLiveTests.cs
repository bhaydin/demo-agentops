using System.Text;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swankers.Web.Coach;

namespace Swankers.Web.Tests;

/// <summary>
/// Talks to the deployed Coach through its agent endpoint (read-only question). Skipped unless
/// FOUNDRY_PROJECT_ENDPOINT is set, so build.yml stays offline; run locally after `az login`.
/// </summary>
public sealed class CoachChatLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_deployed_coach_answers_through_its_endpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), "FOUNDRY_PROJECT_ENDPOINT is not set; this test talks to the deployed Coach.");
        var ct = TestContext.Current.CancellationToken;

        var options = Options.Create(new CoachEndpointOptions
        {
            ProjectEndpoint = endpoint!,
            AgentName = Environment.GetEnvironmentVariable("COACH_AGENT_NAME") is { Length: > 0 } name ? name : "Coach",
        });
        var project = new AIProjectClient(new Uri(endpoint!), new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true }));
        var chat = new CoachChat(project, options);
        var versions = new AgentVersionInfo(project, options, TimeProvider.System, NullLogger<AgentVersionInfo>.Instance);

        // COACH_LIVE_MESSAGE overrides the question ("||" separates turns of one conversation),
        // e.g. to make the Coach attempt a gated action while the web app is open and watch the
        // approval dialog appear.
        var messages = (Environment.GetEnvironmentVariable("COACH_LIVE_MESSAGE") is { Length: > 0 } m
            ? m
            : "In one sentence: who is my starting quarterback this week?").Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var summary = await versions.GetAsync(ct);
        var session = await chat.StartSessionAsync(ct);
        var reply = new StringBuilder();
        output.WriteLine($"{summary.AgentName} v{summary.Version} ({summary.Description}; prompt {summary.PromptVersion}, {summary.Credential})");
        foreach (var message in messages)
        {
            reply.Clear();
            await foreach (var chunk in chat.StreamAsync(session, message, ct))
            {
                reply.Append(chunk);
            }

            output.WriteLine($"> {message}\n{reply}");
        }
        Assert.Null(summary.Error);
        Assert.NotNull(summary.Version);
        Assert.False(string.IsNullOrWhiteSpace(reply.ToString()));
    }
}
