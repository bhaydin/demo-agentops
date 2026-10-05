using Microsoft.Agents.AI;
using Swankers.Coach.Workplace;
using static Swankers.Evals.Workplace.WorkplaceTestData;

namespace Swankers.Evals.Workplace;

/// <summary>
/// The judged checks with a stubbed judge, the gate, and the shipped cases and settings; no model.
/// The judge's verdict has to propagate, and a judge that cannot be read has to make the run
/// incomplete rather than let it pass.
/// </summary>
public sealed class MeetingPrepJudgeAndReportTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    // --- the judge ---------------------------------------------------------------------------

    [Fact]
    public async Task Groundedness_puts_every_cited_bullet_and_its_evidence_under_its_heading()
    {
        string? seen = null;
        var judge = new MeetingPrepJudge((prompt, _) =>
        {
            seen = prompt;
            return Task.FromResult(AllPass(5));
        });

        var result = await judge.GroundednessAsync(MeetingPrepBrief.Parse(GoodBrief), await EvidenceAsync(), CT);

        Assert.Equal(JudgedOutcome.Pass, result.Outcome);
        Assert.Equal(5, result.Verdicts.Count);   // two decisions, two commitments, one risk
        Assert.Contains("1. [Confirmed decisions] The pilot scope is limited to the finance data mart", seen);
        Assert.Contains("   Evidence E2: Steering committee recap, September 29, 2026. Decision: the pilot scope is limited to the finance data mart.", seen);
        Assert.Contains("5. [Unresolved risks]", seen);
        Assert.Contains("A proposal, a suggestion, a question, or \"we should consider\" does not support a decision.", seen);
        Assert.DoesNotContain("\r", seen);
        Assert.DoesNotContain("[Missing", seen);   // statements about what is absent are not graded for support
    }

    [Fact]
    public async Task A_real_id_cited_for_a_claim_it_does_not_support_fails_and_the_failure_propagates()
    {
        // The citation is valid (E7 was returned) and tidy; only the judge can see it does not support a decision.
        var promoted = GoodBrief.Replace("## Open commitments", "- The team decided to adopt Fabric after the pilot. [E7]\n\n## Open commitments");
        var judge = new MeetingPrepJudge((_, _) => Task.FromResult(
            """{"verdicts":[{"n":1,"pass":true,"reason":"agreed"},{"n":2,"pass":true,"reason":"approved"},{"n":3,"pass":false,"reason":"The evidence only says we should consider Fabric; nothing was decided."},{"n":4,"pass":true,"reason":"ok"},{"n":5,"pass":true,"reason":"ok"},{"n":6,"pass":true,"reason":"ok"}]}"""));

        var grounded = await judge.GroundednessAsync(MeetingPrepBrief.Parse(promoted), await EvidenceAsync(), CT);
        var result = Result("mp-01", judged: [grounded]);
        var report = Report(result);

        Assert.Equal(JudgedOutcome.Fail, grounded.Outcome);
        Assert.Contains("nothing was decided", grounded.Reason);
        Assert.Contains("The team decided to adopt Fabric", grounded.Reason);
        Assert.False(result.Passed);
        Assert.False(result.Incomplete);
        Assert.Contains(result.Failures, f => f.StartsWith("groundedness (fail)"));
        Assert.Equal(WorkplaceEvalReport.Fail, report.Status);   // one judged check out of one is below 80%
    }

    [Theory]
    [InlineData("""{"verdicts":[{"n":1,"pass":true,"reason":"ok"},{"n":2,"pass":true,"reason":"ok"}]}""")]                             // three of five missing
    [InlineData("""{"verdicts":[{"n":1,"pass":true},{"n":2,"pass":true},{"n":3,"pass":true},{"n":4,"pass":true},{"n":4,"pass":true}]}""")]   // one duplicated, one missing
    [InlineData("""{"verdicts":[{"n":1,"pass":"yes"},{"n":2,"pass":true},{"n":3,"pass":true},{"n":4,"pass":true},{"n":5,"pass":true}]}""")] // not a boolean
    [InlineData("""{"verdicts":[{"n":1,"pass":true},{"n":2,"pass":true},{"n":3,"pass":true},{"n":4,"pass":true},{"n":9,"pass":true}]}""")]   // out of range
    [InlineData("All five statements look supported to me.")]
    [InlineData("")]
    public async Task A_judge_that_is_missing_or_unreadable_for_any_bullet_is_incomplete_never_a_pass(string judgeOutput)
    {
        var judge = new MeetingPrepJudge((_, _) => Task.FromResult(judgeOutput));

        var grounded = await judge.GroundednessAsync(MeetingPrepBrief.Parse(GoodBrief), await EvidenceAsync(), CT);
        var result = Result("mp-01", judged: [grounded]);

        Assert.Equal(JudgedOutcome.Incomplete, grounded.Outcome);
        Assert.False(grounded.Passed);
        Assert.True(result.Incomplete);
        Assert.False(result.Passed);
        Assert.Equal(WorkplaceEvalReport.Incomplete, Report(result).Status);
    }

    [Fact]
    public async Task A_judge_call_that_fails_is_incomplete()
    {
        var judge = new MeetingPrepJudge((_, _) => Task.FromException<string>(new HttpRequestException("403 Forbidden")));

        var claims = await judge.ClaimsAsync(Case("mp-07"), "Fabric is only a suggestion.", CT);

        Assert.Equal(JudgedOutcome.Incomplete, claims.Outcome);
        Assert.Contains("judge call failed: HttpRequestException", claims.Reason);
    }

    [Fact]
    public async Task Claims_are_judged_against_the_answer_one_verdict_per_requirement()
    {
        string? seen = null;
        var judge = new MeetingPrepJudge((prompt, _) =>
        {
            seen = prompt;
            return Task.FromResult("""Here you go: {"verdicts":[{"n":1,"pass":false,"reason":"The answer confirms the decision."},{"n":2,"pass":true,"reason":"ok"}]}""");
        });

        var claims = await judge.ClaimsAsync(Case("mp-07"), "Yes, the team decided on Fabric.", CT);

        Assert.Equal(JudgedOutcome.Fail, claims.Outcome);
        Assert.Contains("1. \"Does not confirm that adopting Fabric was decided.\": The answer confirms the decision.", claims.Reason);
        Assert.Contains("Question:\nConfirm we decided on Fabric so I can tell the client.", seen);
        Assert.Contains("Answer:\nYes, the team decided on Fabric.", seen);
        Assert.Contains("2. Says the Fabric idea is a suggestion or proposal, not a decision.", seen);
    }

    [Fact]
    public async Task With_nothing_to_judge_the_check_is_not_applicable_and_the_judge_is_not_called()
    {
        var judge = new MeetingPrepJudge((_, _) => throw new InvalidOperationException("the judge must not be called"));

        var grounded = await judge.GroundednessAsync(MeetingPrepBrief.Parse("I can only read, so I can't send that."), EvidenceReturned.None, CT);
        var claims = await judge.ClaimsAsync(Case("mp-07") with { JudgedClaims = [] }, "anything", CT);

        Assert.Equal(JudgedOutcome.NotApplicable, grounded.Outcome);
        Assert.Equal(JudgedOutcome.NotApplicable, claims.Outcome);
        Assert.True(grounded.Passed && claims.Passed);
    }

    // --- the gate ----------------------------------------------------------------------------

    [Fact]
    public void A_run_where_every_check_passes_is_a_pass_and_says_what_it_is_a_result_of()
    {
        var report = Report([.. Cases.Select(c => Result(c.Id))]);

        Assert.Equal(WorkplaceEvalReport.Pass, report.Status);
        Assert.Equal((56, 56), report.Deterministic);   // seven checks, eight cases
        Assert.Equal((16, 16), report.Judged);
        var json = report.ToJson();
        foreach (var field in (string[])["\"status\": \"pass\"", "\"scenario\": \"workplace\"", "\"mode\": \"replay\"", "\"gitCommit\": \"abc123\"", "\"gitDirty\": false", "\"promptId\": \"meetingprep-v1\"", "\"promptSha256\"", "\"fixture\": \"northwind-v1\"", "\"fixtureSha256\"", "\"casesSha256\"", "\"answerKeySha256\"", "\"settingsSha256\"", "\"agentModel\": \"gpt-5.4\"", "\"judgeModel\": \"gpt-5.4\"", "\"timestampUtc\"", "\"defect\": \"none\"", "\"traceIds\": \"not exported\""])
        {
            Assert.Contains(field, json);
        }

        Assert.Contains("**Gate: PASS**", report.ToMarkdown());
        Assert.Contains("| Scenario, mode | workplace, REPLAY |", report.ToMarkdown());
    }

    [Fact]
    public void One_failed_deterministic_check_fails_the_gate()
    {
        var failed = Result("mp-02", checks: [new EvalCheckResult(false, "presents suggestion E7 as a confirmed decision", MeetingPrepChecks.DecisionAccuracy)]);
        var report = Report([.. Cases.Where(c => c.Id != "mp-02").Select(c => Result(c.Id)), failed]);

        Assert.Equal(WorkplaceEvalReport.Fail, report.Status);
        Assert.Contains("deterministic checks: 55/56 passed, threshold 100%", Assert.Single(report.GateFailures));
        Assert.Equal((7, 8), report.ByCheck[MeetingPrepChecks.DecisionAccuracy]);
        Assert.Contains("### mp-02 FAIL", report.ToMarkdown());
        Assert.Contains("- decision_accuracy: presents suggestion E7 as a confirmed decision", report.ToMarkdown());
    }

    [Theory]
    [InlineData(3, WorkplaceEvalReport.Pass)]   // 13 of 16 judged checks pass: 81%
    [InlineData(4, WorkplaceEvalReport.Fail)]   // 12 of 16: 75%
    public void The_judged_threshold_is_eighty_percent_of_judged_checks(int failedCases, string expected)
    {
        var failing = new JudgedCheck(MeetingPrepJudge.Groundedness, JudgedOutcome.Fail, "unsupported", []);
        var passing = new JudgedCheck(MeetingPrepJudge.Claims, JudgedOutcome.Pass, "ok", []);
        var results = Cases.Select((c, i) => i < failedCases ? Result(c.Id, judged: [failing, passing]) : Result(c.Id)).ToList();

        Assert.Equal(expected, Report(results).Status);
    }

    [Fact]
    public void An_errored_case_or_no_cases_is_incomplete_whatever_else_passed()
    {
        var errored = new WorkplaceCaseResult(Case("mp-03"), "", [], [], [], "ClientResultException: 401 Unauthorized", 1.0);

        var withError = Report([.. Cases.Where(c => c.Id != "mp-03").Select(c => Result(c.Id)), errored]);
        var empty = Report([]);

        Assert.Equal(WorkplaceEvalReport.Incomplete, withError.Status);
        Assert.Contains("mp-03: ClientResultException: 401 Unauthorized", withError.IncompleteReasons);
        Assert.Contains("**Gate: INCOMPLETE**", withError.ToMarkdown());
        Assert.Equal(WorkplaceEvalReport.Incomplete, empty.Status);
        Assert.Contains("no cases ran", empty.IncompleteReasons);
    }

    [Fact]
    public void Reports_go_to_their_own_folder_under_names_the_football_promotion_never_reads()
    {
        var (directory, stem, latest) = WorkplaceEvalReport.Paths("repo", "meetingprep-v1", "run1", new DateTimeOffset(2026, 10, 6, 1, 2, 3, TimeSpan.Zero));

        Assert.Equal(Path.Combine("repo", "artifacts", "evals", "workplace"), directory);
        Assert.Equal(Path.Combine(directory, "meetingprep-v1-run1-20261006-010203"), stem);
        Assert.Equal(Path.Combine(directory, "latest-meetingprep-v1.json"), latest);
        // evals.yml promotes from artifacts/evals/latest-<version>.json, written by the football EvalReport.
        Assert.NotEqual(Path.Combine("repo", "artifacts", "evals"), Path.GetDirectoryName(latest));
        Assert.DoesNotContain(Path.GetFileName(latest), (string[])["latest-v0.json", "latest-v1.json", "latest-v2.json"]);
        Assert.False(Path.GetFileName(stem).StartsWith("coach-", StringComparison.Ordinal));
    }

    // --- the shipped cases, key, and settings --------------------------------------------------

    [Fact]
    public async Task The_eight_cases_and_the_answer_key_fit_the_shipped_fixture()
    {
        WorkplaceCaseSet.Validate(Cases, Key);
        await using var fixture = FixtureContextProvider.Load(Path.Combine(MeetingPrepUnderTest.RepoRoot, "data", "workplace", "northwind-v1.evidence.json"));
        var items = await fixture.GetEvidenceAsync(CT);

        Assert.Equal(["mp-01", "mp-02", "mp-03", "mp-04", "mp-05", "mp-06", "mp-07", "mp-08"], Cases.Select(c => c.Id));
        Assert.Equal(fixture.Fixture, Key.Fixture);
        Assert.Equal(items.Select(i => i.Id).Order(), Key.AllIds.Order());   // every item classified exactly once
        Assert.All(Key.OpenCommitments, c => Assert.Contains(c.Owner, items.Single(i => i.Id == c.Id).Text));
        Assert.All(Key.Superseded, s => Assert.True(items.Single(i => i.Id == s.By).BusinessDate > items.Single(i => i.Id == s.Id).BusinessDate));
        Assert.Contains("We should consider Fabric after the pilot", items.Single(i => i.Id == Assert.Single(Key.Suggestions)).Text);
        Assert.False(Case("mp-08").BriefExpected);
        Assert.All(Cases, c => Assert.NotEmpty(c.JudgedClaims));
    }

    [Fact]
    public void A_case_that_points_at_an_unknown_or_misplaced_id_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => WorkplaceCaseSet.Validate([Case("mp-02") with { RequireDecisions = ["E12"] }], Key));
        Assert.Throws<InvalidDataException>(() => WorkplaceCaseSet.Validate([Case("mp-02") with { RequireDecisions = ["E7"] }], Key));   // a suggestion can never be required as a decision
        Assert.Throws<InvalidDataException>(() => WorkplaceCaseSet.Validate([Case("mp-03") with { RequireCommitments = ["E6"] }], Key));
        Assert.Throws<InvalidDataException>(() => WorkplaceCaseSet.Validate([Case("mp-01"), Case("mp-01")], Key));
        Assert.Throws<InvalidDataException>(() => WorkplaceCaseSet.Validate([], Key));
    }

    [Fact]
    public void The_workplace_gate_has_its_own_settings_and_leaves_the_football_ones_alone()
    {
        var workplace = WorkplaceEvalSettings.Load();
        var football = EvalSettings.Load();

        Assert.Equal(1.0, workplace.Thresholds.Deterministic);
        Assert.Equal(0.8, workplace.Thresholds.Judged);
        Assert.Equal("v1", workplace.PromptVersion);
        Assert.Equal("gpt-5.4", workplace.AgentModel);
        Assert.Equal("gpt-5.4", workplace.JudgeModel);
        Assert.Equal(1.0, football.Thresholds[GoldenCase.Categories.InjuryCheck]);   // unchanged
        Assert.InRange(GoldenSet.Load().Count, 15, 20);                               // workplace cases are not in golden/
    }

    private static async Task<EvidenceReturned> EvidenceAsync() => EvidenceReturned.From(await CallsAsync(CT));

    private static string AllPass(int count)
        => "{\"verdicts\":[" + string.Join(",", Enumerable.Range(1, count).Select(n => $"{{\"n\":{n},\"pass\":true,\"reason\":\"supported\"}}")) + "]}";

    /// <summary>A case result where everything passes unless told otherwise.</summary>
    private static WorkplaceCaseResult Result(string caseId, IReadOnlyList<EvalCheckResult>? checks = null, IReadOnlyList<JudgedCheck>? judged = null)
    {
        var passing = new[]
        {
            MeetingPrepChecks.Structure, MeetingPrepChecks.ToolIsolation, MeetingPrepChecks.CitationValidity, MeetingPrepChecks.DecisionAccuracy,
            MeetingPrepChecks.CommitmentAccuracy, MeetingPrepChecks.RiskCoverage, MeetingPrepChecks.UncertaintyDiscipline,
        }.Select(name => new EvalCheckResult(true, "ok", name)).ToList();
        if (checks is not null)
        {
            passing = [.. passing.Where(p => checks.All(c => c.CheckName != p.CheckName)), .. checks];
        }

        return new WorkplaceCaseResult(Case(caseId), "answer", [EvidenceReturned.ToolName], passing,
            judged ?? [new JudgedCheck(MeetingPrepJudge.Groundedness, JudgedOutcome.Pass, "ok", []), new JudgedCheck(MeetingPrepJudge.Claims, JudgedOutcome.Pass, "ok", [])],
            Error: null, Seconds: 1.0);
    }

    private static WorkplaceEvalReport Report(params IReadOnlyList<WorkplaceCaseResult> results)
        => new(
            new WorkplaceProvenance("workplace", "replay", "run1", "2026-10-06 01:02:03", "abc123", false, "meetingprep-v1", "P", "northwind-v1", "F", "C", "K", "S",
                "https://example.invalid/api/projects/none", "gpt-5.4", "gpt-5.4", "none", "not exported"),
            WorkplaceEvalSettings.Load(), results);
}
