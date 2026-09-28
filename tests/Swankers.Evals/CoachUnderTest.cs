extern alias McpServer;

using System.Net.Http.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach;
using Swankers.Coach.Knowledge;
using Swankers.Coach.Mcp;
using Swankers.Coach.Prompts;
using Swankers.League;

namespace Swankers.Evals;

/// <summary>
/// The Coach exactly as the hosted version builds it (CoachAgentFactory, versioned instructions,
/// MCP tools, knowledge search), but with the MCP server in-process, seeded from the repo's real
/// snapshot with snapshot injuries (no live MFL), so every case starts from the same league.
/// The model is the shared Foundry deployment; the caller's Azure identity is used.
/// </summary>
public sealed class CoachUnderTest : IAsyncDisposable
{
    private const string OwnerFranchiseId = "0001";

    private readonly WebApplicationFactory<McpServer::Program> _mcp;
    private readonly HttpClient _http;
    private readonly McpToolSource _toolSource;
    private readonly string _adminKey;
    private readonly string _stateRoot;

    private CoachUnderTest(
        WebApplicationFactory<McpServer::Program> mcp, HttpClient http, McpToolSource toolSource, string adminKey,
        string stateRoot, AIAgent agent, IReadOnlyList<AITool> tools, AIProjectClient project)
    {
        _mcp = mcp;
        _http = http;
        _toolSource = toolSource;
        _adminKey = adminKey;
        _stateRoot = stateRoot;
        Agent = agent;
        Tools = tools;
        Project = project;
    }

    public AIAgent Agent { get; }

    public IReadOnlyList<AITool> Tools { get; }

    public AIProjectClient Project { get; }

    public static string RepoRoot => RepoPaths.FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Run from a checkout: SwankersCoach.slnx not found above the test binaries.");

    public static async Task<CoachUnderTest> StartAsync(string promptVersion, string projectEndpoint, string modelDeployment, CancellationToken ct)
    {
        var repoRoot = RepoRoot;
        var stateRoot = Path.Combine(Path.GetTempPath(), "swankers-evals", Path.GetRandomFileName());
        var ownerCredential = "owner-" + Guid.NewGuid().ToString("N");
        var adminKey = "admin-" + Guid.NewGuid().ToString("N");

        var mcp = new WebApplicationFactory<McpServer::Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:OwnerCredential", ownerCredential);
            b.UseSetting("Mcp:CommissionerCredential", "commissioner-" + Guid.NewGuid().ToString("N"));
            b.UseSetting("Mcp:DemoAdminKey", adminKey);
            b.UseSetting("Mcp:OwnerFranchiseId", OwnerFranchiseId);
            b.UseSetting("Mcp:SnapshotRoot", Path.Combine(repoRoot, "data", "snapshot"));
            b.UseSetting("Mcp:ScenarioRoot", Path.Combine(repoRoot, "data", "demo"));
            b.UseSetting("Mcp:StateDirectory", Path.Combine(stateRoot, "sim"));
        });
        var http = mcp.CreateClient();
        http.DefaultRequestHeaders.Add("X-Demo-Admin-Key", adminKey);

        var options = new CoachOptions
        {
            ProjectEndpoint = projectEndpoint,
            ModelDeployment = modelDeployment,
            PromptVersion = promptVersion,
            McpEndpoint = new Uri(http.BaseAddress!, "/mcp").ToString(),
            AgentName = $"Coach-eval-{promptVersion}",
        };

        var toolSource = new McpToolSource(options, ownerCredential, NullLoggerFactory.Instance, http);
        var leagueTools = await toolSource.ConnectAsync(ct);
        var knowledge = KnowledgeSearch.Load(Path.Combine(repoRoot, "knowledge"));
        var tools = new List<AITool>(leagueTools) { knowledge.AsTool() };
        var instructions = new PromptLibrary(Path.Combine(repoRoot, "src", "Swankers.Coach", "prompts")).Load(promptVersion);

        var agent = CoachAgentFactory.Create(options, instructions, tools, NullLoggerFactory.Instance);
        var project = new AIProjectClient(new Uri(projectEndpoint), new CachedTokenCredential(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })));

        return new CoachUnderTest(mcp, http, toolSource, adminKey, stateRoot, agent, tools, project);
    }

    /// <summary>Restores SimLeague to the snapshot and seeds the case's scenario, if any.</summary>
    public async Task PrepareAsync(GoldenCase golden, CancellationToken ct)
    {
        using var reset = await _http.PostAsync("/api/admin/reset", content: null, ct);
        reset.EnsureSuccessStatusCode();
        if (!string.IsNullOrEmpty(golden.Scenario))
        {
            using var seed = await _http.PostAsync($"/api/admin/seed/{golden.Scenario}", content: null, ct);
            seed.EnsureSuccessStatusCode();
        }
    }

    /// <summary>One fresh conversation per case.</summary>
    public Task<AgentResponse> RunAsync(GoldenCase golden, CancellationToken ct)
        => Agent.RunAsync(golden.Query, session: null, options: null, cancellationToken: ct);

    /// <summary>A no-tools judge agent on the same deployment for the pushback rubric.</summary>
    public Func<string, CancellationToken, Task<string>> CreateJudge(string model)
    {
        var judge = Project.AsAIAgent(model: model, instructions: "You are a strict grader. Answer with JSON only.", name: "PushbackJudge");
        return async (prompt, ct) => (await judge.RunAsync(prompt, session: null, options: null, cancellationToken: ct)).Text;
    }

    public async ValueTask DisposeAsync()
    {
        await _toolSource.DisposeAsync();
        _http.Dispose();
        await _mcp.DisposeAsync();
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }
}
