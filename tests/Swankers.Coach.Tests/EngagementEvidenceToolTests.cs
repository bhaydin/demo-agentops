using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// get_engagement_evidence, the workplace agent's one tool: the same surface whichever provider is
/// behind it, mode carried with the data, a metadata-only span, and a failure that stays a failure.
/// </summary>
public sealed class EngagementEvidenceToolTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(-5));

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public void Is_exposed_under_its_stable_name_and_takes_no_arguments()
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Tool(new StubProvider()).AsTool());

        Assert.Equal("get_engagement_evidence", tool.Name);
        Assert.Contains("cite evidence by id", tool.Description);
        var properties = tool.JsonSchema.TryGetProperty("properties", out var p) ? p.EnumerateObject().Select(x => x.Name).ToList() : [];
        Assert.Empty(properties);   // nothing for the model to widen the query with
    }

    [Fact]
    public async Task Returns_the_evidence_with_the_mode_engagement_and_reference_time()
    {
        var result = await Tool(new StubProvider()).GetEngagementEvidenceAsync(CT);

        Assert.Equal("replay", result.Mode);
        Assert.Equal("NW-DPM", result.Engagement);
        Assert.Equal("2026-10-07T09:00:00-05:00", result.ReferenceTime);
        Assert.Equal("stub-v1", result.Fixture);
        Assert.Equal(["E1", "E2"], result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task The_model_sees_the_mode_when_it_calls_the_tool()
    {
        var function = (AIFunction)Tool(new StubProvider()).AsTool();

        var result = await function.InvokeAsync(cancellationToken: CT);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("replay", json);
        Assert.Contains("2026-10-07T09:00:00-05:00", json);
        Assert.Contains("fixture:stub-v1#E1", json);
    }

    [Fact]
    public async Task Emits_a_span_with_metadata_and_no_content()
    {
        var stopped = Listen();
        using var scope = new Activity("test.workplace.tool").Start();

        await Tool(new StubProvider()).GetEngagementEvidenceAsync(CT);

        var span = Assert.Single(stopped.Value, a => a.RootId == scope.RootId);
        Assert.Equal(EngagementEvidenceTool.SpanName, span.OperationName);
        Assert.Equal("workplace", span.GetTagItem(WorkplaceTags.Scenario));
        Assert.Equal("replay", span.GetTagItem(WorkplaceTags.Mode));
        Assert.Equal("stub-v1", span.GetTagItem(WorkplaceTags.Fixture));
        Assert.Equal(2, span.GetTagItem(WorkplaceTags.Items));
        var tagText = string.Join(" ", span.TagObjects.Select(t => t.Value?.ToString()));
        Assert.DoesNotContain("finance data mart", tagText);
        Assert.DoesNotContain("Steering", tagText);
        stopped.Key.Dispose();
    }

    [Fact]
    public async Task A_live_failure_stays_a_failure()
    {
        var stopped = Listen();
        using var scope = new Activity("test.workplace.tool.failure").Start();
        var tool = Tool(new StubProvider { Mode = WorkplaceModes.Live, Fixture = null, Failure = new IOException("Work IQ is unreachable.") });

        // No fallback exists: the tool throws, and the agent reports that retrieval failed.
        var ex = await Assert.ThrowsAsync<IOException>(() => tool.GetEngagementEvidenceAsync(CT));

        Assert.Equal("Work IQ is unreachable.", ex.Message);
        var span = Assert.Single(stopped.Value, a => a.RootId == scope.RootId);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("live", span.GetTagItem(WorkplaceTags.Mode));
        Assert.Null(span.GetTagItem(WorkplaceTags.Fixture));
        Assert.Null(span.GetTagItem(WorkplaceTags.Items));
        stopped.Key.Dispose();
    }

    private static EngagementEvidenceTool Tool(IWorkplaceContextProvider provider)
        => new(provider, new WorkplaceOptions(), ReferenceTime);

    private static KeyValuePair<ActivityListener, List<Activity>> Listen()
    {
        var stopped = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CoachDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return new(listener, stopped);
    }

    private sealed class StubProvider : IWorkplaceContextProvider
    {
        public string Mode { get; init; } = WorkplaceModes.Replay;

        public string? Fixture { get; init; } = "stub-v1";

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<EvidenceItem>> GetEvidenceAsync(CancellationToken cancellationToken)
            => Failure is not null
                ? Task.FromException<IReadOnlyList<EvidenceItem>>(Failure)
                : Task.FromResult<IReadOnlyList<EvidenceItem>>(
                [
                    new("E1", "email", "[NW-DPM] Kickoff notes", "Priya Raman", null, "Planned pilot go-live: November 2, 2026.", "fixture:stub-v1#E1", null, Synthetic: true),
                    new("E2", "email", "[NW-DPM] Steering committee recap", "Priya Raman", null, "Decision: the pilot scope is limited to the finance data mart.", "fixture:stub-v1#E2", null, Synthetic: true),
                ]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
