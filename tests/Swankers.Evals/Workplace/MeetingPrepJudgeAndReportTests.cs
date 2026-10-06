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

    // --- the judge configuration (judge-v2) ------------------------------------------------------

    [Fact]
    public void The_judge_configuration_is_versioned_and_hashed_apart_from_the_prompt()
    {
        var config = MeetingPrepJudgeConfig.Current;

        Assert.Equal("judge-v2", config.Version);
        Assert.Matches("^[0-9A-F]{64}$", config.Sha256);
        Assert.Equal(config.Sha256, (config with { }).Sha256);
        // Any change to what the judge is told is a different configuration.
        Assert.NotEqual(config.Sha256, (config with { Version = "judge-v3" }).Sha256);
        Assert.NotEqual(config.Sha256, (config with { SystemInstruction = config.SystemInstruction + " Be lenient." }).Sha256);
        Assert.NotEqual(config.Sha256, (config with { GroundednessInstructions = config.GroundednessInstructions.Replace("only when", "when") }).Sha256);
        Assert.NotEqual(config.Sha256, (config with { ClaimsInstructions = config.ClaimsInstructions + "\n" }).Sha256);
        Assert.DoesNotContain("\r", config.GroundednessInstructions + config.ClaimsInstructions);
        Assert.Same(config, new MeetingPrepJudge((_, _) => Task.FromResult("")).Config);
    }

    [Fact]
    public void The_judge_is_told_that_a_faithful_paraphrase_passes_and_exactly_what_fails()
    {
        var rules = MeetingPrepJudgeConfig.Current.GroundednessInstructions;

        // The evaluator defect of the first baseline: a paraphrase was failed for its wording.
        Assert.Contains("Pass a statement that is a faithful account of its evidence. A paraphrase is faithful", rules);
        Assert.Contains("\"the kickoff date\" for \"the date in the kickoff notes\" is a faithful paraphrase", rules);
        // Fail only for one of four things.
        Assert.Contains("Fail a statement only when it does at least one of these:", rules);
        Assert.Contains("- adds a fact the evidence does not state", rules);
        Assert.Contains("- changes a fact", rules);
        Assert.Contains("- materially strengthens what the evidence says", rules);
        Assert.Contains("- contradicts the evidence.", rules);
        // Ranking a risk the evidence does not rank is named as strengthening.
        Assert.Contains("ranking or prioritizing something (the biggest, the clearest, the most important) when the evidence gives no such ranking", rules);
        // The standard for each heading is unchanged.
        Assert.Contains("A proposal, a suggestion, a question, or \"we should consider\" does not support a decision.", rules);
        Assert.Contains("Evidence that it was completed does not support an open commitment.", rules);
        Assert.DoesNotContain("strict", MeetingPrepJudgeConfig.Current.SystemInstruction);
    }

    [Fact]
    public async Task Each_verdict_stays_with_its_statement_a_paraphrase_passing_and_a_ranking_or_strengthened_claim_failing()
    {
        // A stub stands in for the model, so this shows the plumbing, not the judge's judgement:
        // the real judge's handling of these three kinds of statement is JudgeCalibration's job.
        var brief = MeetingPrepBrief.Parse("""
            ## Confirmed decisions
            - The pilot go-live was approved to move from November 2, 2026 to November 16, 2026, replacing the kickoff date. [E5]

            ## Unresolved risks
            - The source ERP extract is missing ledger mappings for two business units. This is the clearest risk to raise. [E4]
            - The missing ledger mappings will delay the November 16 go-live. [E4]
            """);
        string? seen = null;
        var judge = new MeetingPrepJudge((prompt, _) =>
        {
            seen = prompt;
            return Task.FromResult("""{"verdicts":[{"n":1,"pass":true,"reason":"A faithful paraphrase of the approved change."},{"n":2,"pass":false,"reason":"The evidence does not rank this risk."},{"n":3,"pass":false,"reason":"The evidence states no effect on go-live."}]}""");
        });

        var grounded = await judge.GroundednessAsync(brief, await EvidenceAsync(), CT);

        Assert.StartsWith(MeetingPrepJudgeConfig.Current.GroundednessInstructions, seen);
        Assert.Equal(JudgedOutcome.Fail, grounded.Outcome);
        Assert.Equal([true, false, false], grounded.Verdicts.Select(v => v.Pass));
        Assert.Contains("replacing the kickoff date", grounded.Verdicts[0].Statement);
        Assert.Contains("clearest risk to raise", grounded.Verdicts[1].Statement);
        Assert.Contains("will delay", grounded.Verdicts[2].Statement);
        Assert.DoesNotContain("replacing the kickoff date", grounded.Reason);   // only the failed statements are reported as failures
        Assert.Contains("The evidence does not rank this risk.", grounded.Reason);
        Assert.Contains("The evidence states no effect on go-live.", grounded.Reason);
    }

    [Fact]
    public async Task The_calibration_set_is_well_formed_and_reaches_the_judge_in_its_labeled_order()
    {
        // Model-free: this checks the labeled statements, not the judge. JudgeCalibration sends them.
        var evidence = await EvidenceAsync();
        var labels = JudgeCalibration.InJudgedOrder;
        string? seen = null;
        var judge = new MeetingPrepJudge((prompt, _) =>
        {
            seen = prompt;
            return Task.FromResult(AllPass(labels.Count));
        });

        var result = await judge.GroundednessAsync(JudgeCalibration.AsBrief(), evidence, CT);

        Assert.Equal(JudgeCalibration.Statements.Count, labels.Count);
        Assert.Equal(labels.Count, result.Verdicts.Count);
        Assert.All(labels, label => Assert.Contains(label.Cites, evidence.Items.Keys));
        for (var i = 0; i < labels.Count; i++)
        {
            Assert.Contains($"{i + 1}. [{labels[i].Heading}] {labels[i].Statement}", seen);
        }

        // All three kinds the calibration must prove are present.
        Assert.Contains(labels, l => l.ShouldPass && l.Kind.StartsWith("paraphrase", StringComparison.Ordinal));
        Assert.Contains(labels, l => !l.ShouldPass && l.Kind.StartsWith("unsupported ranking", StringComparison.Ordinal));
        Assert.Contains(labels, l => !l.ShouldPass && l.Kind.StartsWith("materially strengthened", StringComparison.Ordinal));

        // The deterministic check agrees with the labels on ranking: it flags the two ranked risks and nothing else.
        var ranking = MeetingPrepChecks.CheckRiskCharacterization(JudgeCalibration.AsBrief(), evidence);
        Assert.False(ranking.Passed);
        Assert.Contains("\"clearest\"", ranking.Reason);
        Assert.Contains("\"most important\"", ranking.Reason);
        Assert.Equal(2, ranking.Reason.Split("ranks a risk with").Length - 1);
    }

    // --- the gate ----------------------------------------------------------------------------

    [Fact]
    public void A_run_where_every_check_passes_is_a_pass_and_says_what_it_is_a_result_of()
    {
        var report = Report([.. Cases.Select(c => Result(c.Id))]);

        Assert.Equal(WorkplaceEvalReport.Pass, report.Status);
        Assert.Equal((64, 64), report.Deterministic);   // eight checks, eight cases
        Assert.Equal((16, 16), report.Judged);
        var json = report.ToJson();
        foreach (var field in (string[])["\"status\": \"pass\"", "\"scenario\": \"workplace\"", "\"mode\": \"replay\"", "\"gitCommit\": \"abc123\"", "\"gitDirty\": false", "\"promptId\": \"meetingprep-v1\"", "\"promptSha256\"", "\"fixture\": \"northwind-v1\"", "\"fixtureSha256\"", "\"casesSha256\"", "\"answerKeySha256\"", "\"settingsSha256\"", "\"agentModel\": \"gpt-5.4\"", "\"judgeModel\": \"gpt-5.4\"", "\"judgeConfig\": \"judge-v2\"", "\"judgeConfigSha256\": \"J\"", "\"timestampUtc\"", "\"defect\": \"none\"", "\"traceIds\": \"not exported\""])
        {
            Assert.Contains(field, json);
        }

        Assert.Contains("**Gate: PASS**", report.ToMarkdown());
        Assert.Contains("| Scenario, mode | workplace, REPLAY |", report.ToMarkdown());
        Assert.Contains("| Judge configuration | judge-v2, sha256 J |", report.ToMarkdown());
    }

    [Fact]
    public void One_failed_deterministic_check_fails_the_gate()
    {
        var failed = Result("mp-02", checks: [new EvalCheckResult(false, "presents suggestion E7 as a confirmed decision", MeetingPrepChecks.DecisionAccuracy)]);
        var report = Report([.. Cases.Where(c => c.Id != "mp-02").Select(c => Result(c.Id)), failed]);

        Assert.Equal(WorkplaceEvalReport.Fail, report.Status);
        Assert.Contains("deterministic checks: 63/64 passed, threshold 100%", Assert.Single(report.GateFailures));
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_model_backed_run_must_name_its_prompt_version_and_never_runs_on_a_default(string? configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkplaceEvalSettings.ExplicitPromptVersion(configured));

        Assert.Contains("WORKPLACE_PROMPT_VERSION must be set explicitly", ex.Message);
        Assert.Equal("v1.1", WorkplaceEvalSettings.ExplicitPromptVersion(" v1.1 "));
        Assert.Equal("v2", WorkplaceEvalSettings.ExplicitPromptVersion("v2"));
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
            MeetingPrepChecks.CommitmentAccuracy, MeetingPrepChecks.RiskCoverage, MeetingPrepChecks.RiskCharacterization, MeetingPrepChecks.UncertaintyDiscipline,
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
                "https://example.invalid/api/projects/none", "gpt-5.4", "gpt-5.4", "judge-v2", "J", "none", "not exported"),
            WorkplaceEvalSettings.Load(), results);
}
