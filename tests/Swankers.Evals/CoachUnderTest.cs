using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach;
using Swankers.Coach.Knowledge;
using Swankers.Coach.Prompts;

namespace Swankers.Evals;

/// <summary>
/// The Coach as the evals see it: the real agent factory, the versioned prompt, the knowledge
/// tool, and the league tools from <see cref="McpServerUnderTest"/> (Swankers.Mcp in-process on
/// the real snapshot). Only the model is remote.
/// </summary>
public sealed class CoachUnderTest : IAsyncDisposable
{
    private readonly McpServerUnderTest _league;

    private CoachUnderTest(McpServerUnderTest league, AIAgent agent, IReadOnlyList<AITool> tools, AIProjectClient project)
    {
        _league = league;
        Agent = agent;
        Tools = tools;
        Project = project;
    }

    public AIAgent Agent { get; }

    public IReadOnlyList<AITool> Tools { get; }

    public AIProjectClient Project { get; }

    public static string RepoRoot => McpServerUnderTest.RepoRoot;

    public static async Task<CoachUnderTest> StartAsync(string promptVersion, string projectEndpoint, string modelDeployment, CancellationToken ct)
    {
        var repoRoot = RepoRoot;
        var league = await McpServerUnderTest.StartAsync(ct);

        var options = new CoachOptions
        {
            ProjectEndpoint = projectEndpoint,
            ModelDeployment = modelDeployment,
            PromptVersion = promptVersion,
            McpEndpoint = league.McpEndpoint,
            AgentName = $"Coach-eval-{promptVersion}",
        };

        var knowledge = KnowledgeSearch.Load(Path.Combine(repoRoot, "knowledge"));
        var tools = new List<AITool>(league.Tools) { knowledge.AsTool() };
        var instructions = new PromptLibrary(Path.Combine(repoRoot, "src", "Swankers.Coach", "prompts")).Load(promptVersion);

        var agent = CoachAgentFactory.Create(options, instructions, tools, NullLoggerFactory.Instance);
        var project = new AIProjectClient(new Uri(projectEndpoint), new CachedTokenCredential(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })));

        return new CoachUnderTest(league, agent, tools, project);
    }

    /// <summary>Restores SimLeague to the snapshot and seeds the case's scenario, if any.</summary>
    public Task PrepareAsync(GoldenCase golden, CancellationToken ct) => _league.ResetAsync(golden.Scenario, ct);

    /// <summary>One fresh conversation per case.</summary>
    public Task<AgentResponse> RunAsync(GoldenCase golden, CancellationToken ct)
        => Agent.RunAsync(golden.Query, session: null, options: null, cancellationToken: ct);

    /// <summary>A no-tools judge agent on the same deployment for the pushback rubric.</summary>
    public Func<string, CancellationToken, Task<string>> CreateJudge(string model)
    {
        var judge = Project.AsAIAgent(model: model, instructions: "You are a strict grader. Answer with JSON only.", name: "PushbackJudge");
        return async (prompt, ct) => (await judge.RunAsync(prompt, session: null, options: null, cancellationToken: ct)).Text;
    }

    public ValueTask DisposeAsync() => _league.DisposeAsync();
}
