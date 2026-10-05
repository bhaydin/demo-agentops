using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The provider against an in-memory MCP server that offers the kinds of tool the pinned Work IQ
/// server lists. No Work IQ, no sign-in, no model: this covers the allow-list, the default query,
/// the tag filter, references, failures, and the span. It does not show that Work IQ works; only
/// the live smoke test does.
/// </summary>
public sealed class WorkIqContextProviderTests
{
    private const string ReadArguments = """{"entityUrls":["/me/messages"]}""";

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_every_tool_and_allows_only_listed_read_tools()
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch", "create_entity"], readTool: "fetch");

        var tools = await provider.ConnectAsync(CT);

        Assert.Equal(["accept_eula", "ask", "create_entity", "do_action", "fetch", "fetch_denied", "fetch_forbidden"], tools.Select(t => t.Name).Order());
        Assert.Equal(["fetch"], tools.Where(t => t.Allowed).Select(t => t.Name));
        Assert.All(tools, t => Assert.Equal(System.Text.Json.JsonValueKind.Object, t.InputSchema.ValueKind));
    }

    [Fact]
    public async Task With_nothing_configured_only_fetch_is_allowed_and_the_inbox_query_is_what_gets_sent()
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = new WorkIqContextProvider(new WorkplaceOptions(), NullLoggerFactory.Instance, workIq.ClientTransport);

        var tools = await provider.ConnectAsync(CT);
        var evidence = await provider.GetEvidenceAsync(CT);

        Assert.Equal(["fetch"], tools.Where(t => t.Allowed).Select(t => t.Name));
        Assert.Equal(
            ["/me/mailFolders/inbox/messages?$filter=contains(subject,%27%5BNW-DPM%5D%27)&$select=id,subject,receivedDateTime,from,bodyPreview,webLink&$top=25"],
            workIq.LastEntityUrls);
        Assert.Equal(["E1", "E2"], evidence.Select(e => e.Id));
        Assert.Equal(1, workIq.Calls);
    }

    [Fact]
    public async Task Returns_tagged_evidence_in_time_order_with_references_unchanged()
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch"], readTool: "fetch");
        await provider.ConnectAsync(CT);

        var evidence = await provider.GetEvidenceAsync(CT);

        Assert.Equal(["E1", "E2"], evidence.Select(e => e.Id));
        Assert.Equal(["synthetic-msg-1", "synthetic-msg-2"], evidence.Select(e => e.SourceRef));
        Assert.Equal("https://example.invalid/mail/2", evidence[1].SourceUrl);
        Assert.DoesNotContain(evidence, e => e.Title == "Lunch on Friday?");   // untagged mail never reaches the model
    }

    [Fact]
    public async Task Emits_one_tool_span_with_metadata_and_no_content()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CoachDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch"], readTool: "fetch");
        await provider.ConnectAsync(CT);

        // The listener is process-wide; count only spans under this test's own activity.
        using var scope = new Activity("test.workiq").Start();
        await provider.GetEvidenceAsync(CT);
        var span = Assert.Single(stopped, a => a.RootId == scope.RootId);

        Assert.Equal("workiq.tool fetch", span.OperationName);
        Assert.Equal("workplace", span.GetTagItem(WorkplaceTags.Scenario));
        Assert.Equal("live", span.GetTagItem(WorkplaceTags.Mode));
        Assert.Equal("fetch", span.GetTagItem(WorkplaceTags.ToolName));
        Assert.Equal(2, span.GetTagItem(WorkplaceTags.Items));
        Assert.Equal(1, span.GetTagItem(WorkplaceTags.ItemsDropped));
        var tagText = string.Join(" ", span.TagObjects.Select(t => t.Value?.ToString()));
        Assert.DoesNotContain("finance data mart", tagText);
        Assert.DoesNotContain("synthetic-msg", tagText);
        Assert.DoesNotContain("/me/messages", tagText);
    }

    [Theory]
    [InlineData("create_entity")]   // listed, but a mutation
    [InlineData("ask")]             // a read tool, but not listed
    [InlineData("no_such_tool")]
    public async Task Refuses_a_read_tool_that_is_not_allow_listed(string readTool)
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch", "create_entity"], readTool);
        await provider.ConnectAsync(CT);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEvidenceAsync(CT));

        Assert.Contains("not an allow-listed read tool", ex.Message);
        Assert.Equal(0, workIq.Calls);
    }

    [Fact]
    public async Task A_tool_error_is_an_error_not_empty_evidence()
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch_denied"], readTool: "fetch_denied");
        await provider.ConnectAsync(CT);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEvidenceAsync(CT));

        Assert.Contains("returned an error", ex.Message);
    }

    [Fact]
    public async Task A_refused_path_is_an_error_not_empty_evidence()
    {
        // The tool call succeeds; the per-path status inside the result says the read was refused.
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch_forbidden"], readTool: "fetch_forbidden");
        await provider.ConnectAsync(CT);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEvidenceAsync(CT));

        Assert.Contains("HTTP 403", ex.Message);
    }

    [Fact]
    public async Task Evidence_before_connecting_is_refused()
    {
        await using var workIq = new FakeWorkIq();
        await using var provider = Provider(workIq, allowed: ["fetch"], readTool: "fetch");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEvidenceAsync(CT));
    }

    private static WorkIqContextProvider Provider(FakeWorkIq workIq, string[] allowed, string readTool)
        => new(
            new WorkplaceOptions { WorkIq = new WorkIqOptions { AllowedTools = allowed, ReadTool = readTool, ReadArgumentsJson = ReadArguments } },
            NullLoggerFactory.Instance,
            workIq.ClientTransport);

    /// <summary>An MCP server over in-memory pipes with read tools, mutation tools, and one that fails.</summary>
    private sealed class FakeWorkIq : IAsyncDisposable
    {
        private readonly Pipe _toServer = new();
        private readonly Pipe _toClient = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly McpServer _server;
        private readonly Task _running;
        private int _calls;

        public FakeWorkIq()
        {
            var tools = new McpServerPrimitiveCollection<McpServerTool>
            {
                McpServerTool.Create((string[] entityUrls) =>
                {
                    LastEntityUrls = entityUrls;
                    return Called(WorkIqEvidenceMapperTests.FetchResult);
                }, new McpServerToolCreateOptions { Name = "fetch" }),
                McpServerTool.Create((string[] entityUrls) => Called("""{"results":[{"data":{"error":{"code":"ErrorAccessDenied"}},"statusCode":403}]}"""), new McpServerToolCreateOptions { Name = "fetch_forbidden" }),
                McpServerTool.Create((string eulaUrl) => Called("""{"accepted":true}"""), new McpServerToolCreateOptions { Name = "accept_eula" }),
                McpServerTool.Create((string question) => Called("""{"response":"synthetic answer","conversationId":"synthetic"}"""), new McpServerToolCreateOptions { Name = "ask" }),
                McpServerTool.Create((string parentUrl, string jsonBody) => Called("""{"statusCode":201}"""), new McpServerToolCreateOptions { Name = "create_entity" }),
                McpServerTool.Create((string actionUrl) => Called("""{"statusCode":202}"""), new McpServerToolCreateOptions { Name = "do_action" }),
                McpServerTool.Create(string (string[] entityUrls) => throw new McpException("Blocked by tenant policy."), new McpServerToolCreateOptions { Name = "fetch_denied" }),
            };

            _server = McpServer.Create(
                new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream(), "fake-work-iq", NullLoggerFactory.Instance),
                new McpServerOptions { ServerInfo = new Implementation { Name = "fake-work-iq", Version = "0.0.0" }, ToolCollection = tools },
                NullLoggerFactory.Instance,
                serviceProvider: null);
            _running = _server.RunAsync(_stop.Token);
        }

        /// <summary>Successful tool invocations, to prove a refused tool was never reached.</summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <summary>The paths the last fetch was asked for.</summary>
        public IReadOnlyList<string>? LastEntityUrls { get; private set; }

        public IClientTransport ClientTransport
            => new StreamClientTransport(_toServer.Writer.AsStream(), _toClient.Reader.AsStream(), NullLoggerFactory.Instance);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _running;
            }
            catch (OperationCanceledException)
            {
            }

            await _server.DisposeAsync();
            _stop.Dispose();
        }

        private string Called(string result)
        {
            Interlocked.Increment(ref _calls);
            return result;
        }
    }
}
