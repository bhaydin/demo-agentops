using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>The golden set and settings as shipped; no model involved.</summary>
public sealed class GoldenSetTests
{
    [Fact]
    public void Ships_fifteen_to_twenty_cases_across_the_four_categories()
    {
        var cases = GoldenSet.Load();

        Assert.InRange(cases.Count, 15, 20);
        foreach (var category in GoldenCase.Categories.All)
        {
            Assert.True(cases.Count(c => c.Category == category) >= 4, $"{category} has fewer than 4 cases");
        }
    }

    [Fact]
    public void Injury_cases_require_the_news_check_and_adversarial_cases_forbid_irreversible_tools()
    {
        var cases = GoldenSet.Load();

        Assert.All(cases.Where(c => c.Category == GoldenCase.Categories.InjuryCheck), c =>
        {
            Assert.NotEmpty(c.NewsCheckFor);
            Assert.Contains("get_player_news", c.ExpectedTools);
        });
        Assert.All(cases.Where(c => c.Category == GoldenCase.Categories.Adversarial), c =>
        {
            Assert.True(c.OwnFranchiseOnly);
            Assert.True(c.ForbiddenTools.Contains("drop_player") || c.ForbiddenTools.Contains("respond_to_trade") || c.ForbiddenTools.Contains("set_lineup"), $"{c.Id} forbids no write tool");
        });
        Assert.All(cases.Where(c => c.Category == GoldenCase.Categories.Pushback), c => Assert.NotEmpty(c.ForbiddenTools));
    }

    [Fact]
    public void Pairwise_start_sit_cases_score_the_recommendation_against_their_choices()
    {
        var pairwise = GoldenSet.Load().Where(c => c.Choices.Count > 0).ToList();

        Assert.Equal(4, pairwise.Count);
        Assert.All(pairwise, c =>
        {
            Assert.Equal(2, c.Choices.Count);
            Assert.Contains(c.ExpectedOutput, c.Choices);
            Assert.Equal(2, c.NewsCheckFor.Count);
        });
        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([pairwise[0] with { ExpectedOutput = "Somebody" }]));
        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([pairwise[0] with { Choices = ["Only"] }]));
    }

    [Fact]
    public void Scenarios_referenced_by_cases_exist_in_the_repo()
    {
        var demo = Path.Combine(CoachUnderTest.RepoRoot, "data", "demo");

        foreach (var scenario in GoldenSet.Load().Select(c => c.Scenario).Where(s => s is not null).Distinct())
        {
            Assert.True(File.Exists(Path.Combine(demo, scenario + ".json")), $"missing data/demo/{scenario}.json");
        }
    }

    [Fact]
    public void Validation_rejects_duplicates_and_unknown_categories()
    {
        var a = new GoldenCase { Id = "a", Category = GoldenCase.Categories.StartSit, Query = "q1" };

        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([a, a with { Query = "q2" }]));
        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([a, a with { Id = "b" }]));
        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([a with { Category = "vibes" }]));
        Assert.Throws<InvalidDataException>(() => GoldenSet.Validate([a with { Category = GoldenCase.Categories.InjuryCheck }]));
    }

    [Fact]
    public void Settings_load_with_a_threshold_per_category()
    {
        var settings = EvalSettings.Load();

        Assert.Equal(1.0, settings.Thresholds[GoldenCase.Categories.InjuryCheck]);
        Assert.Equal(1.0, settings.Thresholds[GoldenCase.Categories.Adversarial]);
        Assert.Contains("task_adherence", settings.Foundry.Evaluators);
        Assert.True(settings.Foundry.IsGated("task_adherence"));
        Assert.False(settings.Foundry.IsGated("tool_call_accuracy"), "tool_call_accuracy is report-only");
        Assert.Equal("gpt-5.4", settings.JudgeModel);
    }

    [Fact]
    public void Foundry_gate_uses_only_the_gated_evaluators()
    {
        var settings = EvalSettings.Load();
        var cases = GoldenSet.Load();
        var allPass = cases.Select(c => new CaseResult(c, "answer", [], [], c.Category == GoldenCase.Categories.Pushback ? new PushbackEvaluator.Verdict(true, 2, true, "ok") : null, null)).ToList();
        var foundry = new FoundrySummary("completed", [], new Dictionary<string, (int, int)>
        {
            ["tool_call_accuracy"] = (7, 8),
            ["intent_resolution"] = (16, 1),
            ["task_adherence"] = (17, 0),
        }, null);

        var report = new EvalReport("v1", settings, allPass, foundry);
        var adherenceRegressed = new EvalReport("v1", settings, allPass, foundry with { PerEvaluator = new Dictionary<string, (int, int)> { ["task_adherence"] = (10, 7) } });

        Assert.True(report.Passed, string.Join("; ", report.GateFailures));
        Assert.Contains("tool_call_accuracy: 7 passed, 8 failed (report only)", report.ToMarkdown());
        Assert.False(adherenceRegressed.Passed);
        Assert.Contains("foundry task_adherence: 10/17", Assert.Single(adherenceRegressed.GateFailures));
    }

    [Fact]
    public void Report_gates_on_thresholds_and_names_the_failed_cases()
    {
        var settings = EvalSettings.Load();
        var cases = GoldenSet.Load();
        var results = cases.Select(c => new CaseResult(
            c, "answer", [],
            c.Id == "ic-02" ? [new Microsoft.Agents.AI.EvalCheckResult(false, "get_player_news was not called for Judkins", "news_before_recommendation")] : [],
            c.Category == GoldenCase.Categories.Pushback ? new PushbackEvaluator.Verdict(true, 2, true, "ok") : null,
            null)).ToList();

        var report = new EvalReport("v2", settings, results, foundry: null);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.GateFailures);
        Assert.StartsWith("injury_check: 75%", failure);
        Assert.Contains("ic-02", failure);
        Assert.Contains("not called for Judkins", failure);
        Assert.Contains("| injury_check | 3/4 (75%) | 100% |", report.ToMarkdown());
    }
}
