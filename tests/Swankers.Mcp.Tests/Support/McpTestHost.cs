using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Swankers.League.Models;
using Swankers.League.Snapshots;
using Swankers.Mcp.Api;

namespace Swankers.Mcp.Tests.Support;

/// <summary>
/// The MCP server hosted in-process over a fresh synthetic snapshot in a temp directory,
/// with known credentials. Each instance is an isolated league.
/// </summary>
public sealed class McpTestHost : IAsyncDisposable
{
    public const string OwnerCredential = "owner-test-credential";
    public const string CommissionerCredential = "commissioner-test-credential";
    public const string AdminKey = "admin-test-key";

    private readonly string _root;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<McpClient> _clients = [];

    private McpTestHost(string root, WebApplicationFactory<Program> factory)
    {
        _root = root;
        _factory = factory;
    }

    /// <summary>Where this host's SimLeague persists state.json (tests can block it to force save failures).</summary>
    public string StateDirectory => Path.Combine(_root, "sim");

    /// <summary>This host's snapshot store (tests can add a newer snapshot to simulate a re-capture).</summary>
    public string SnapshotRoot => Path.Combine(_root, "snapshot");

    /// <param name="mflResponder">
    /// When set, MFL is treated as configured and every MFL request is answered by this stub
    /// (e.g. a 503 to exercise the snapshot fallback). Null leaves MFL unconfigured.
    /// </param>
    public static async Task<McpTestHost> StartAsync(
        bool commissionerGateEnabled = true,
        bool ownerGateEnabled = true,
        Func<HttpResponseMessage>? mflResponder = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "swankers-mcp-tests", Path.GetRandomFileName());
        var snapshotRoot = Path.Combine(root, "snapshot");
        await new SnapshotStore(snapshotRoot).SaveAsync(BuildSnapshot(), CancellationToken.None);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:OwnerCredential", OwnerCredential);
            b.UseSetting("Mcp:CommissionerCredential", CommissionerCredential);
            b.UseSetting("Mcp:DemoAdminKey", AdminKey);
            b.UseSetting("Mcp:OwnerFranchiseId", "0001");
            b.UseSetting("Mcp:OwnerGateEnabled", ownerGateEnabled.ToString());
            b.UseSetting("Mcp:CommissionerGateEnabled", commissionerGateEnabled.ToString());
            b.UseSetting("Mcp:SnapshotRoot", snapshotRoot);
            b.UseSetting("Mcp:StateDirectory", Path.Combine(root, "sim"));
            b.UseSetting("Mcp:ScenarioRoot", Path.Combine(RepoRoot(), "data", "demo"));

            if (mflResponder is not null)
            {
                b.UseSetting("Mfl:ApiKey", "test-key");
                b.UseSetting("Mfl:LeagueId", "12345");
                b.UseSetting("Mfl:Host", "www42.myfantasyleague.com");
                b.UseSetting("Mfl:UserAgent", "SwankersTest/1.0");
                b.ConfigureTestServices(services => services
                    .AddHttpClient<Swankers.League.Mfl.MflExportClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(mflResponder)));
            }
        });

        return new McpTestHost(root, factory);
    }

    private sealed class StubHttpHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder());
    }

    /// <summary>An MCP client authenticated with the given credential (null = no Authorization header).</summary>
    public async Task<McpClient> ConnectAsync(string? credential, CancellationToken ct)
    {
        var http = _factory.CreateClient();
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            AdditionalHeaders = credential is null
                ? null
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {credential}" },
        };
        var transport = new HttpClientTransport(options, http, NullLoggerFactory.Instance, false);
        var client = await McpClient.CreateAsync(transport, null, null, ct);
        _clients.Add(client);
        return client;
    }

    /// <summary>Plain HTTP client for the demo REST API, with or without the admin key.</summary>
    public HttpClient Http(bool withAdminKey = true)
    {
        var http = _factory.CreateClient();
        if (withAdminKey)
        {
            http.DefaultRequestHeaders.Add(DemoEndpoints.AdminKeyHeader, AdminKey);
        }

        return http;
    }

    public async Task<JsonElement> ResolveAsync(string confirmationId, bool approve, CancellationToken ct)
    {
        using var response = await Http().PostAsJsonAsync(
            $"/api/confirmations/{confirmationId}", new ConfirmationDecision(approve), ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct));
    }

    public static async Task<JsonElement> CallAsync(
        McpClient client, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        // IsError is bool? and stays null on success.
        Assert.True(result.IsError != true, "tool returned an error: " + Text(result));
        return JsonSerializer.Deserialize<JsonElement>(Text(result));
    }

    public static async Task<string> CallExpectingErrorAsync(
        McpClient client, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        Assert.True(result.IsError == true, "expected a tool error but got: " + Text(result));
        return Text(result);
    }

    public static string Text(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    /// <summary>Case-insensitive property lookup: the SDK serializes results in camelCase.</summary>
    public static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new KeyNotFoundException($"No property '{name}' in {element}");
    }

    public static IEnumerable<string> PlayerIds(JsonElement rosterView)
        => Prop(rosterView, "players").EnumerateArray().Select(p => Prop(p, "id").GetString()!);

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _factory.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwankersCoach.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>Synthetic league: 0001 (owner), 0002, and The Fleecers (0099). Week 4 is current.</summary>
    public static Snapshot BuildSnapshot(string id = "2026-09-27", DateTimeOffset? capturedAt = null) => new(
        new SnapshotManifest(id, capturedAt ?? new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), 4, "synthetic"),
        [new Franchise("0001", "The Swank"), new Franchise("0002", "Mock Dynasty"), new Franchise("0099", "The Fleecers", IsSimOnly: true)],
        [
            new Player("1001", "Synthetic, Quin", "QB", "GBP"),
            new Player("1002", "Fake, Runner", "RB", "CHI"),
            new Player("1003", "Test, Wide", "WR", "FA"),
            new Player("1004", "Sample, Tight", "TE", "DET"),
            new Player("1005", "Bench, Back", "RB", "MIN"),
        ],
        [
            new Roster("0001", [new RosterSlot("1001", RosterStatus.Roster), new RosterSlot("1002", RosterStatus.Roster), new RosterSlot("1005", RosterStatus.Roster)]),
            new Roster("0002", [new RosterSlot("1004", RosterStatus.InjuredReserve)]),
            new Roster("0099", [new RosterSlot("1003", RosterStatus.Roster)]),
        ],
        [new Injury("1004", "Questionable", "Hamstring", "Oct 4, 2026")],
        [new Matchup(3, "0002", "0001", 98.2m, 101.5m), new Matchup(4, "0001", "0002", null, null)],
        [new Projection("1001", 4, 18.4m), new Projection("1002", 4, 11.2m), new Projection("1005", 4, 4.1m)],
        [new WeeklyResult(3, "0001", 101.5m, [new PlayerResult("1001", 24.1m, true)])],
        [new Standing("0001", 3, 1, 0, 512.4m, 455.1m), new Standing("0002", 1, 3, 0, 401.0m, 480.2m)],
        [],
        []);
}
