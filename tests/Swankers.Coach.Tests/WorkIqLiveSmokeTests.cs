using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The W1 gate (docs/BUILD-PLAN.md): the application-side C# path against the real, pinned Work IQ
/// CLI under the maintainer's own sign-in. Opt-in with WORKIQ_LIVE=1 and run by the maintainer;
/// CI never sets it, and a skip is a skip, not a pass. Raw captures go to the machine's local
/// application data, never under the checkout (see <see cref="LiveCaptures"/>); the test output
/// carries tool names, counts, and dates, never content.
/// Everything but the opt-in defaults to the configuration verified on 2026-10-05
/// (demo/winet-workiq-evidence.md): fetch only, the Inbox query for the tag, nine items.
///
///   WORKIQ_LIVE=1            opt in
///   WORKIQ_ACCOUNT           the signed-in account to use (default: the CLI's default account)
///   WORKIQ_COMMAND           the CLI (default: workiq)
///   WORKIQ_ARGS              arguments, space separated (default: mcp, plus the account if set)
///   WORKIQ_ALLOWED_TOOLS     comma-separated exact tool names (default: fetch)
///   WORKIQ_READ_TOOL         the allow-listed tool that reads the corpus (default: fetch)
///   WORKIQ_READ_ARGS         that tool's arguments as a JSON object (default: the Inbox query)
///   WORKIQ_TAG               the corpus tag (default: NW-DPM)
///   WORKIQ_EXPECT_ITEMS      the least number of corpus items expected (default: 9)
///   WORKIQ_CAPTURE_ROOT      where raw captures are written (default: local application data)
/// </summary>
public sealed class WorkIqLiveSmokeTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_the_tools_the_pinned_server_offers()
    {
        Assert.SkipUnless(Live, "Opt-in live test: set WORKIQ_LIVE=1 after installing and signing in to the pinned Work IQ CLI.");

        var stderr = new List<string>();
        await using var provider = new WorkIqContextProvider(Options(), NullLoggerFactory.Instance) { StandardErrorSink = stderr.Add };

        var clock = Stopwatch.StartNew();
        var tools = await provider.ConnectAsync(CT);
        clock.Stop();

        var path = Capture("tools", JsonSerializer.Serialize(tools, Indented));
        Capture("stderr-connect", string.Join(Environment.NewLine, stderr));
        output.WriteLine($"Connected and listed {tools.Count} tools in {clock.ElapsedMilliseconds} ms; {stderr.Count} stderr lines. Schemas: {path}");
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            output.WriteLine($"  {tool.Name}{(WorkIqToolPolicy.IsMutation(tool.Name) ? " (mutation: never allowed)" : "")}{(tool.Allowed ? " (allowed)" : "")}");
        }

        Assert.NotEmpty(tools);
        Assert.DoesNotContain(tools, t => t.Allowed && WorkIqToolPolicy.IsMutation(t.Name));
    }

    [Fact]
    public async Task Reads_the_tagged_corpus_with_references_and_a_span()
    {
        Assert.SkipUnless(Live, "Opt-in live test: set WORKIQ_LIVE=1 after installing and signing in to the pinned Work IQ CLI.");

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CoachDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var stderr = new List<string>();
        var raw = new List<string>();
        var options = Options();
        await using var provider = new WorkIqContextProvider(options, NullLoggerFactory.Instance) { StandardErrorSink = stderr.Add, RawResultSink = raw.Add };
        await provider.ConnectAsync(CT);

        using var scope = new Activity("test.workiq.live").Start();
        var clock = Stopwatch.StartNew();
        var evidence = await provider.GetEvidenceAsync(CT);
        clock.Stop();

        var rawPath = Capture("raw-result", string.Join(Environment.NewLine, raw));
        Capture("evidence", JsonSerializer.Serialize(evidence, Indented));
        Capture("stderr-read", string.Join(Environment.NewLine, stderr));
        output.WriteLine($"{options.WorkIq.ReadTool}: {evidence.Count} tagged items in {clock.ElapsedMilliseconds} ms; raw result {raw.Sum(r => r.Length)} chars at {rawPath}");
        foreach (var item in evidence)
        {
            output.WriteLine($"  {item.Id}: {item.SourceType}, business date {item.BusinessDate?.ToString("yyyy-MM-dd") ?? "none"}, received {item.ReceivedAt:u}, text {item.Text.Length} chars, ref {(item.SourceRef is null ? "none" : "present")}, link {(item.SourceUrl is null ? "none" : "present")}, synthesized {item.Synthesized}");
        }

        // Chronology comes from the date each item states, not from when the mailbox received it.
        Assert.All(evidence, item => Assert.True(item.BusinessDate is not null, $"{item.Id} states no business date"));

        var expected = int.TryParse(Setting("WORKIQ_EXPECT_ITEMS", "9"), out var n) ? n : 9;
        Assert.True(evidence.Count >= expected, $"expected at least {expected} tagged corpus items, got {evidence.Count}");

        // Retrieval, not an answer: every item is source material with the reference Work IQ returned, unchanged.
        var returned = raw.SelectMany(Strings).ToHashSet(StringComparer.Ordinal);
        Assert.All(evidence, item =>
        {
            Assert.False(item.Synthesized, $"{item.Id} is a synthesized answer, not retrieved source text");
            Assert.False(string.IsNullOrEmpty(item.SourceRef), $"{item.Id} carries no source reference");
            Assert.Contains(item.SourceRef!, returned);
            if (item.SourceUrl is not null)
            {
                Assert.Contains(item.SourceUrl, returned);
            }
        });

        var span = Assert.Single(stopped, a => a.RootId == scope.RootId);
        Assert.Equal($"workiq.tool {options.WorkIq.ReadTool}", span.OperationName);
        Assert.Equal("workplace", span.GetTagItem(WorkplaceTags.Scenario));
        Assert.Equal("live", span.GetTagItem(WorkplaceTags.Mode));
        Assert.Equal(evidence.Count, span.GetTagItem(WorkplaceTags.Items));
        output.WriteLine($"Span {span.OperationName}: trace {span.TraceId}, {span.Duration.TotalMilliseconds:N0} ms, tags {string.Join(", ", span.TagObjects.Select(t => $"{t.Key}={t.Value}"))}");
    }

    private static bool Live => Setting("WORKIQ_LIVE", "") == "1";

    // An unset variable leaves the option at its verified default.
    private static WorkplaceOptions Options() => new()
    {
        EngagementTag = Setting("WORKIQ_TAG", "NW-DPM"),
        WorkIq = new WorkIqOptions
        {
            Command = Setting("WORKIQ_COMMAND", "workiq"),
            Arguments = Setting("WORKIQ_ARGS", "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Account = Setting("WORKIQ_ACCOUNT", ""),
            AllowedTools = Setting("WORKIQ_ALLOWED_TOOLS", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            ReadTool = Setting("WORKIQ_READ_TOOL", WorkIqOptions.FetchTool),
            ReadArgumentsJson = Setting("WORKIQ_READ_ARGS", ""),
        },
    };

    /// <summary>Writes a private capture outside the checkout and returns its path.</summary>
    private static string Capture(string name, string content) => LiveCaptures.Write(name, content);

    /// <summary>Every string value in a JSON result, so a reference can be compared whatever the escaping.</summary>
    private static IEnumerable<string> Strings(string raw)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            return [.. Walk(document.RootElement)];
        }

        static IEnumerable<string> Walk(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => [element.GetString()!],
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Walk(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Walk),
            _ => [],
        };
    }

    private static string Setting(string name, string fallback)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
