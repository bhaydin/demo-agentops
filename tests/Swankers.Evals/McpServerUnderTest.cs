extern alias McpServer;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach;
using Swankers.Coach.Mcp;
using Swankers.League;

namespace Swankers.Evals;

/// <summary>
/// Swankers.Mcp hosted in-process on the real snapshot, with a fresh SimLeague state directory
/// and random credentials, connected through <see cref="McpToolSource"/> exactly as the Coach
/// connects. The tools, and the results they hand the model, are therefore the real ones.
/// </summary>
public sealed class McpServerUnderTest : IAsyncDisposable
{
    public const string OwnerFranchiseId = "0001";

    private readonly WebApplicationFactory<McpServer::Program> _mcp;
    private readonly McpToolSource _toolSource;
    private readonly string _stateRoot;

    private McpServerUnderTest(
        WebApplicationFactory<McpServer::Program> mcp, HttpClient http, McpToolSource toolSource, string stateRoot,
        string mcpEndpoint, IReadOnlyList<AITool> tools)
    {
        _mcp = mcp;
        _toolSource = toolSource;
        _stateRoot = stateRoot;
        Http = http;
        McpEndpoint = mcpEndpoint;
        Tools = tools;
    }

    /// <summary>Client for the demo REST endpoints, with the admin key header set.</summary>
    public HttpClient Http { get; }

    /// <summary>The MCP endpoint the Coach options point at.</summary>
    public string McpEndpoint { get; }

    /// <summary>The league tools as the agent sees them (owner credential).</summary>
    public IReadOnlyList<AITool> Tools { get; }

    public static string RepoRoot => RepoPaths.FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Run from a checkout: SwankersCoach.slnx not found above the test binaries.");

    public static async Task<McpServerUnderTest> StartAsync(CancellationToken ct)
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

        var mcpEndpoint = new Uri(http.BaseAddress!, "/mcp").ToString();
        var options = new CoachOptions { McpEndpoint = mcpEndpoint };
        var toolSource = new McpToolSource(options, ownerCredential, NullLoggerFactory.Instance, http);
        var tools = await toolSource.ConnectAsync(ct);

        return new McpServerUnderTest(mcp, http, toolSource, stateRoot, mcpEndpoint, tools);
    }

    /// <summary>Restores SimLeague to the snapshot and seeds a scenario, if one is named.</summary>
    public async Task ResetAsync(string? scenario, CancellationToken ct)
    {
        using var reset = await Http.PostAsync("/api/admin/reset", content: null, ct);
        reset.EnsureSuccessStatusCode();
        if (!string.IsNullOrEmpty(scenario))
        {
            using var seed = await Http.PostAsync($"/api/admin/seed/{scenario}", content: null, ct);
            seed.EnsureSuccessStatusCode();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _toolSource.DisposeAsync();
        Http.Dispose();
        await _mcp.DisposeAsync();
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }
}
