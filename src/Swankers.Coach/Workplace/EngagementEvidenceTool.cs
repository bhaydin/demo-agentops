using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace Swankers.Coach.Workplace;

/// <summary>
/// What the model receives: the evidence, plus the mode, engagement, and reference time it must
/// reason from. Mode travels with the data so an answer can always say whether it is Live or Replay.
/// </summary>
public sealed record EngagementEvidence(
    string Mode,
    string Engagement,
    string ReferenceTime,
    string? Fixture,
    IReadOnlyList<EvidenceItem> Items);

/// <summary>
/// The get_engagement_evidence tool: the one tool the workplace agent has, read-only and the same
/// in Live and Replay. It takes no arguments, so the model cannot widen the query; the engagement
/// is fixed by configuration. A provider failure surfaces as a tool error.
/// </summary>
public sealed class EngagementEvidenceTool(IWorkplaceContextProvider provider, WorkplaceOptions options, DateTimeOffset referenceTime)
{
    public const string ToolName = "get_engagement_evidence";
    public const string SpanName = "workplace.get_engagement_evidence";

    [Description("Returns the evidence on record for the current client engagement, together with the reference time to treat as now. Each item has an id, source type, title, author, text, source reference, a businessDate (the date the evidence itself states; use it for what happened earlier or later), and a receivedAt (when the mailbox received it; transport metadata only). Call it before answering anything about the engagement, and cite evidence by id.")]
    public async Task<EngagementEvidence> GetEngagementEvidenceAsync(CancellationToken cancellationToken)
    {
        // Metadata only: no evidence text, titles, or references on the span.
        using var activity = CoachDiagnostics.ActivitySource.StartActivity(SpanName);
        activity?.SetTag(WorkplaceTags.Scenario, CoachScenarios.Workplace);
        activity?.SetTag(WorkplaceTags.Mode, provider.Mode);
        if (provider.Fixture is { } fixture)
        {
            activity?.SetTag(WorkplaceTags.Fixture, fixture);
        }

        try
        {
            var items = await provider.GetEvidenceAsync(cancellationToken);
            activity?.SetTag(WorkplaceTags.Items, items.Count);
            return new EngagementEvidence(
                provider.Mode,
                options.EngagementTag,
                referenceTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                provider.Fixture,
                items);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
    }

    public AITool AsTool()
        => AIFunctionFactory.Create(GetEngagementEvidenceAsync, name: ToolName);
}
