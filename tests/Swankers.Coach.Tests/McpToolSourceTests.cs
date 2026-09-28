extern alias McpServer;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach.Mcp;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.Coach.Tests;

/// <summary>Tool discovery against the real MCP server hosted in-process; no model involved.</summary>
public sealed class McpToolSourceTests : IAsyncLifetime
{
    private const string OwnerCredential = "owner-test-credential";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swankers-coach-tests", Path.GetRandomFileName());
    private WebApplicationFactory<McpServer::Program> _mcp = null!;

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var snapshotRoot = Path.Combine(_root, "snapshot");
        await new SnapshotStore(snapshotRoot).SaveAsync(MinimalSnapshot(), CT);
        _mcp = new WebApplicationFactory<McpServer::Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:OwnerCredential", OwnerCredential);
            b.UseSetting("Mcp:CommissionerCredential", "commissioner-test-credential");
            b.UseSetting("Mcp:DemoAdminKey", "admin-test-key");
            b.UseSetting("Mcp:SnapshotRoot", snapshotRoot);
            b.UseSetting("Mcp:StateDirectory", Path.Combine(_root, "sim"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _mcp.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Discovers_the_ten_league_tools_as_functions()
    {
        var http = _mcp.CreateClient();
        await using var source = new McpToolSource(
            new CoachOptions { McpEndpoint = new Uri(http.BaseAddress!, "/mcp").ToString() },
            OwnerCredential, NullLoggerFactory.Instance, http);

        var tools = await source.ConnectAsync(CT);

        Assert.Equal(10, tools.Count);
        Assert.All(tools, t => Assert.IsAssignableFrom<AIFunction>(t));
        Assert.Contains(tools, t => t.Name == "get_player_news");
        Assert.Contains(tools, t => t.Name == "drop_player");
        Assert.DoesNotContain(tools, t => t.Name.Contains("approve") || t.Name.Contains("confirm"));
    }

    [Fact]
    public async Task Wrong_credential_cannot_connect()
    {
        var http = _mcp.CreateClient();
        await using var source = new McpToolSource(
            new CoachOptions { McpEndpoint = new Uri(http.BaseAddress!, "/mcp").ToString() },
            "not-a-credential", NullLoggerFactory.Instance, http);

        await Assert.ThrowsAnyAsync<Exception>(() => source.ConnectAsync(CT));
    }

    private static Snapshot MinimalSnapshot() => new(
        new SnapshotManifest("2026-09-27", new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), 4, "synthetic"),
        [new Franchise("0001", "The Swank"), new Franchise("0099", "The Fleecers", IsSimOnly: true)],
        [new Player("1001", "Synthetic, Quin", "QB", "GBP")],
        [new Roster("0001", [new RosterSlot("1001", RosterStatus.Roster)]), new Roster("0099", [])],
        [], [], [], [], [new Standing("0001", 1, 0, 0, 100m, 90m)], [], []);
}
