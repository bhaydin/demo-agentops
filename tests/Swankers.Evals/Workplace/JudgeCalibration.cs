using Azure.AI.Projects;
using Azure.Identity;
using Swankers.Coach;

namespace Swankers.Evals.Workplace;

/// <summary>
/// Does the real judge, under the current judge configuration, apply the rule it is given? A
/// stubbed judge cannot answer that, so this sends labeled statements to the judge model: faithful
/// paraphrases that must pass, and statements that add, change, materially strengthen (ranking
/// included), or contradict a fact, which must fail. Several are the statements the first
/// meetingprep-v1 baseline turned on. It grades the judge, not the agent: no agent runs here.
/// Opt-in and model-backed (one judge call per repetition); CI never runs it.
///
///   WORKPLACE_JUDGE_CALIBRATION   repetitions to run, for example 3
///   FOUNDRY_PROJECT_ENDPOINT      the Foundry project
/// </summary>
public sealed class JudgeCalibration(ITestOutputHelper output)
{
    /// <summary>A statement under a heading, the evidence it cites, and the verdict a correct judge gives.</summary>
    public sealed record Labeled(string Heading, string Statement, string Cites, bool ShouldPass, string Kind);

    public static readonly IReadOnlyList<Labeled> Statements =
    [
        // Faithful paraphrases: these must pass.
        new(MeetingPrepBrief.Decisions, "The pilot go-live was approved to move from November 2, 2026 to November 16, 2026, replacing the kickoff date.", "E5", true, "paraphrase (failed by judge-v1 in baseline run 3)"),
        new(MeetingPrepBrief.Decisions, "Pilot scope was narrowed to the finance data mart, with the steering committee's agreement and Dana Okafor's confirmation.", "E2", true, "paraphrase"),
        new(MeetingPrepBrief.Commitments, "Priya Raman owes the security review summary; it is due October 9, 2026 and has not arrived.", "E3", true, "paraphrase"),
        new(MeetingPrepBrief.Commitments, "Marcus Lindqvist is to send Dana Okafor the revised statement of work; the evidence gives no due date.", "E8", true, "paraphrase, with a true statement of absence"),
        new(MeetingPrepBrief.Risks, "Ledger mappings for two business units are missing from the source ERP extract, and no one is assigned to fix it.", "E4", true, "paraphrase"),
        new(MeetingPrepBrief.Risks, "The source ERP extract is missing ledger mappings for two business units. Nobody has been assigned to resolve it, so this is an unresolved risk to raise.", "E4", true, "baseline run 2's wording, no ranking"),

        // Adds, changes, materially strengthens, or contradicts: these must fail.
        new(MeetingPrepBrief.Risks, "The source ERP extract is missing ledger mappings for two business units, and nobody has been assigned to resolve it. This is the clearest risk to raise.", "E4", false, "unsupported ranking (baseline runs 1 and 3)"),
        new(MeetingPrepBrief.Risks, "The missing ledger mappings in the ERP extract are the most important risk to the pilot.", "E4", false, "unsupported ranking"),
        new(MeetingPrepBrief.Risks, "The missing ledger mappings will delay the November 16 go-live.", "E4", false, "materially strengthened: adds a consequence"),
        new(MeetingPrepBrief.Decisions, "The team decided to adopt Fabric after the pilot.", "E7", false, "materially strengthened: a suggestion as a decision"),
        new(MeetingPrepBrief.Commitments, "Priya Raman will deliver the security review summary by October 8, 2026.", "E3", false, "changes a fact: the date"),
        new(MeetingPrepBrief.Commitments, "Dana Okafor will send the revised statement of work.", "E8", false, "changes a fact: the owner"),
        new(MeetingPrepBrief.Commitments, "The sandbox access request is still open.", "E6", false, "contradicts the evidence"),
    ];

    /// <summary>The labeled statements as a brief, so they reach the judge by the same path as an agent's answer.</summary>
    public static MeetingPrepBrief AsBrief()
        => MeetingPrepBrief.Parse(string.Join("\n", MeetingPrepBrief.Headings.Take(3).Select(heading =>
            $"## {heading}\n" + string.Join("\n", Statements.Where(s => s.Heading == heading).Select(s => $"- {s.Statement} [{s.Cites}]")) + "\n")));

    /// <summary>The labels in the order the judge numbers the statements (by heading, then as listed).</summary>
    public static IReadOnlyList<Labeled> InJudgedOrder
        => [.. MeetingPrepBrief.Headings.Take(3).SelectMany(heading => Statements.Where(s => s.Heading == heading))];

    [Fact]
    public async Task The_judge_passes_faithful_paraphrases_and_fails_added_changed_strengthened_or_contradicted_facts()
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        var repetitions = int.TryParse(Environment.GetEnvironmentVariable("WORKPLACE_JUDGE_CALIBRATION"), out var n) ? n : 0;
        Assert.SkipUnless(repetitions > 0 && !string.IsNullOrWhiteSpace(endpoint),
            "Opt-in model-backed judge calibration: set WORKPLACE_JUDGE_CALIBRATION to the repetitions and FOUNDRY_PROJECT_ENDPOINT.");

        var ct = TestContext.Current.CancellationToken;
        var settings = WorkplaceEvalSettings.Load();
        var config = MeetingPrepJudgeConfig.Current;
        var project = new AIProjectClient(new Uri(endpoint!), new CachedTokenCredential(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })));
        var judge = new MeetingPrepJudge(MeetingPrepUnderTest.CreateJudge(project, settings.JudgeModel, config), config);

        // The tool's real result for the fixed fixture: the judge reads what it reads in an evaluation run.
        var evidence = EvidenceReturned.From(await WorkplaceTestData.CallsAsync(ct));
        var labels = InJudgedOrder;
        output.WriteLine($"Judge {settings.JudgeModel}, {config.Version} (sha256 {config.Sha256}); {labels.Count} labeled statements; {repetitions} repetition(s).");

        var wrong = new List<string>();
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var result = await judge.GroundednessAsync(AsBrief(), evidence, ct);
            Assert.True(result.Outcome is JudgedOutcome.Pass or JudgedOutcome.Fail, $"repetition {repetition}: the judge could not be read: {result.Reason}");
            Assert.Equal(labels.Count, result.Verdicts.Count);
            for (var i = 0; i < labels.Count; i++)
            {
                var verdict = result.Verdicts[i];
                var agrees = verdict.Pass == labels[i].ShouldPass;
                output.WriteLine($"  r{repetition} {i + 1,2}. {(agrees ? "ok   " : "WRONG")} expected {(labels[i].ShouldPass ? "pass" : "fail")}, judge said {(verdict.Pass ? "pass" : "fail")} [{labels[i].Kind}]: {verdict.Reason}");
                if (!agrees)
                {
                    wrong.Add($"repetition {repetition}, statement {i + 1} ({labels[i].Kind}): expected {(labels[i].ShouldPass ? "pass" : "fail")}, judge said {(verdict.Pass ? "pass" : "fail")}: {verdict.Reason}");
                }
            }
        }

        Assert.True(wrong.Count == 0, "The judge disagreed with the labels:\n" + string.Join("\n", wrong));
    }
}
