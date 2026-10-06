using Microsoft.Extensions.AI;
using Swankers.Coach.Workplace;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals.Workplace;

/// <summary>
/// Hand-built inputs for the model-free workplace tests: a brief that satisfies the contract, and
/// conversations whose tool result is what the real get_engagement_evidence tool returns for the
/// shipped fixture, so the checks are pinned to the real shape rather than a hand-written one.
/// </summary>
internal static class WorkplaceTestData
{
    public static readonly WorkplaceAnswerKey Key = WorkplaceCaseSet.LoadKey();

    public static readonly IReadOnlyList<WorkplaceCase> Cases = WorkplaceCaseSet.LoadCases();

    public static WorkplaceCase Case(string id) => Cases.Single(c => c.Id == id);

    // Line endings are normalized so the text is the same on every checkout.
    public static readonly string GoodBrief = """
        As of 2026-10-07T09:00:00-05:00, engagement NW-DPM, evidence mode replay.

        ## Confirmed decisions
        - The pilot scope is limited to the finance data mart; the steering committee agreed and Dana Okafor confirmed it. [E2]
        - Dana Okafor approved moving the pilot go-live from November 2 to November 16, 2026. [E5]

        ## Open commitments
        - Priya Raman will deliver the security review summary by October 9, 2026; it has not been delivered yet. [E3]
        - Marcus Lindqvist will send the revised statement of work to Dana Okafor; no due date was given. [E8]

        ## Unresolved risks
        - The source ERP extract is missing ledger mappings for two business units; nobody has been assigned to resolve it. [E4]

        ## Missing, conflicting, or stale information
        - The kickoff notes planned go-live for November 2, 2026; that date was replaced by November 16. [E1] [E5]
        - Who signs off on the pilot acceptance criteria is not established. [E9]
        - "We should consider Fabric after the pilot" is an open suggestion, not a decision. [E7]

        ## Sources
        - E2 — [NW-DPM] Steering committee recap — 2026-09-29 — fixture:northwind-v1#E2
        - E5 — [NW-DPM] Go-live date change approved — 2026-10-01 — fixture:northwind-v1#E5
        - E3 — [NW-DPM] Security review summary — 2026-09-30 — fixture:northwind-v1#E3
        - E8 — [NW-DPM] Statement of work — 2026-10-02 — fixture:northwind-v1#E8
        - E4 — [NW-DPM] ERP extract issue — 2026-09-30 — fixture:northwind-v1#E4
        - E1 — [NW-DPM] Kickoff notes — 2026-09-22 — fixture:northwind-v1#E1
        - E9 — [NW-DPM] Acceptance criteria sign-off — 2026-10-02 — fixture:northwind-v1#E9
        - E7 — [NW-DPM] Working session notes — 2026-10-02 — fixture:northwind-v1#E7
        """.ReplaceLineEndings("\n");

    /// <summary>The tool's real result for the shipped fixture, as the agent framework hands it to the model.</summary>
    public static async Task<object?> RealToolResultAsync(CancellationToken cancellationToken)
    {
        var provider = FixtureContextProvider.Load(Path.Combine(MeetingPrepUnderTest.RepoRoot, "data", "workplace", "northwind-v1.evidence.json"));
        var tool = (AIFunction)new EngagementEvidenceTool(provider, new WorkplaceOptions(), WorkplaceScenario.ParseReferenceTime(new WorkplaceOptions().ReferenceTime)).AsTool();
        return await tool.InvokeAsync(cancellationToken: cancellationToken);
    }

    /// <summary>A conversation: the evidence call and its real result (unless told otherwise), any extra calls, then the answer.</summary>
    public static async Task<IReadOnlyList<ToolCall>> CallsAsync(CancellationToken cancellationToken, bool evidenceCall = true, params string[] otherTools)
    {
        var conversation = new List<ChatMessage> { new(ChatRole.User, "question") };
        if (evidenceCall)
        {
            conversation.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-evidence", EvidenceReturned.ToolName, new Dictionary<string, object?>())]));
            conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-evidence", await RealToolResultAsync(cancellationToken))]));
        }

        foreach (var tool in otherTools)
        {
            conversation.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{tool}", tool, new Dictionary<string, object?>())]));
            conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"call-{tool}", """{"status":"ok"}""")]));
        }

        conversation.Add(new ChatMessage(ChatRole.Assistant, "answer"));
        return ToolCall.From(conversation);
    }
}
