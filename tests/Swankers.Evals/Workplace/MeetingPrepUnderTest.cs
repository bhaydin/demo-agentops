using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach;
using Swankers.Coach.Workplace;

namespace Swankers.Evals.Workplace;

/// <summary>
/// The WorkplaceMeetingPrep agent as the evaluation sees it: the real composition
/// (<see cref="WorkplaceScenario"/>), the real agent factory, the checked-in prompt, and the
/// fixture provider. Always Replay: evaluation holds the evidence fixed, and Work IQ is never
/// started here. No league MCP server, no knowledge, and no Responses host, so nothing is
/// persisted; every case runs with a new session. Only the model is remote.
/// </summary>
public sealed class MeetingPrepUnderTest : IAsyncDisposable
{
    private MeetingPrepUnderTest(AIAgent agent, WorkplaceComposition composition, AIProjectClient project, string agentModel)
    {
        Agent = agent;
        Composition = composition;
        Project = project;
        AgentModel = agentModel;
    }

    public AIAgent Agent { get; }

    public WorkplaceComposition Composition { get; }

    public AIProjectClient Project { get; }

    public string AgentModel { get; }

    public IReadOnlyList<string> ExposedTools => [.. Composition.Tools.Select(t => t.Name)];

    public FixtureContextProvider Fixture => (FixtureContextProvider)Composition.Provider;

    public static string RepoRoot => McpServerUnderTest.RepoRoot;

    public static string PromptsDirectory => Path.Combine(RepoRoot, "src", "Swankers.Coach", "prompts");

    public static async Task<MeetingPrepUnderTest> StartAsync(string promptVersion, string projectEndpoint, string agentModel, CancellationToken cancellationToken)
    {
        var composition = await WorkplaceScenario.ComposeAsync(
            new WorkplaceOptions { Mode = WorkplaceModes.Replay }, promptVersion, PromptsDirectory, RepoRoot, NullLoggerFactory.Instance, cancellationToken);

        var options = new CoachOptions
        {
            Scenario = CoachScenarios.Workplace,
            ProjectEndpoint = projectEndpoint,
            ModelDeployment = agentModel,
            PromptVersion = promptVersion,
            AgentName = WorkplaceScenario.AgentNameFor("MeetingPrep-eval", promptVersion),
        };
        var agent = CoachAgentFactory.Create(options, composition.Instructions, composition.Tools, NullLoggerFactory.Instance, WorkplaceScenario.Description);
        var project = new AIProjectClient(new Uri(projectEndpoint), new CachedTokenCredential(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })));

        return new MeetingPrepUnderTest(agent, composition, project, agentModel);
    }

    /// <summary>One fresh conversation per case: no session is passed, so nothing carries over.</summary>
    public Task<AgentResponse> RunAsync(WorkplaceCase @case, CancellationToken cancellationToken)
        => Agent.RunAsync(@case.Query, session: null, options: null, cancellationToken: cancellationToken);

    /// <summary>A no-tools judge agent under the given judge configuration; each call is its own conversation.</summary>
    public Func<string, CancellationToken, Task<string>> CreateJudge(string model, MeetingPrepJudgeConfig? config = null)
        => CreateJudge(Project, model, config ?? MeetingPrepJudgeConfig.Current);

    public static Func<string, CancellationToken, Task<string>> CreateJudge(AIProjectClient project, string model, MeetingPrepJudgeConfig config)
    {
        var judge = project.AsAIAgent(model: model, instructions: config.SystemInstruction, name: "MeetingPrepJudge");
        return async (prompt, ct) => (await judge.RunAsync(prompt, session: null, options: null, cancellationToken: ct)).Text;
    }

    public ValueTask DisposeAsync() => Composition.Provider.DisposeAsync();
}
