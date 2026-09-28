using Microsoft.Agents.AI;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>Codex Phase 5 P2: the recommendation itself is scored, not the presence of a name. Stub judge; no model.</summary>
public sealed class RecommendationExtractorTests
{
    private static readonly string[] Choices = ["Judkins", "Dowdle"];

    private static readonly GoldenCase Pairwise = new()
    {
        Id = "ss-x", Category = GoldenCase.Categories.StartSit, Query = "Judkins or Dowdle?",
        Choices = Choices, ExpectedOutput = "Judkins",
    };

    [Fact]
    public void Normalize_maps_judge_text_onto_a_choice_or_neither()
    {
        Assert.Equal("Judkins", RecommendationExtractor.Normalize("""{"recommended":"Judkins","rationale":"x"}""", Choices));
        Assert.Equal("Dowdle", RecommendationExtractor.Normalize("Sure:\n```json\n{\"recommended\": \"Rico Dowdle\"}\n```", Choices));
        Assert.Equal("Judkins", RecommendationExtractor.Normalize("judkins", Choices));
        Assert.Equal(RecommendationExtractor.Neither, RecommendationExtractor.Normalize("""{"recommended":"neither"}""", Choices));
        Assert.Equal(RecommendationExtractor.Both, RecommendationExtractor.Normalize("""{"recommended":"both"}""", Choices));
        Assert.Equal(RecommendationExtractor.Neither, RecommendationExtractor.Normalize("I cannot tell.", Choices));
    }

    [Fact]
    public async Task The_prompt_carries_the_choices_the_question_and_the_reply()
    {
        string? prompt = null;
        var extractor = new RecommendationExtractor((p, _) => { prompt = p; return Task.FromResult("""{"recommended":"Judkins"}"""); });

        var recommended = await extractor.ExtractAsync("Judkins or Dowdle?", "Start Judkins.", Choices, TestContext.Current.CancellationToken);

        Assert.Equal("Judkins", recommended);
        Assert.Contains("Judkins, Dowdle", prompt);
        Assert.Contains("Judkins or Dowdle?", prompt);
        Assert.Contains("Start Judkins.", prompt);
    }

    [Fact]
    public async Task A_reversed_recommendation_fails_even_though_it_mentions_the_expected_player()
    {
        // The judge is stubbed to read the reply literally: it names Dowdle as the start.
        var extractor = new RecommendationExtractor((_, _) => Task.FromResult("""{"recommended":"Dowdle"}"""));
        var item = ToolSequenceEvaluatorTests.Item(Pairwise.Query, "Bench Judkins. Start Rico Dowdle even though he is Out.");

        var checks = await new ToolSequenceEvaluator().EvaluateAsync(Pairwise, item, extractor, TestContext.Current.CancellationToken);

        var recommendation = Assert.Single(checks, c => c.CheckName == "recommendation");
        Assert.False(recommendation.Passed);
        Assert.Contains("recommends Dowdle, expected Judkins", recommendation.Reason);
        Assert.Equal("n/a", Assert.Single(checks, c => c.CheckName == "expected_output").Reason);
    }

    [Fact]
    public async Task The_expected_recommendation_passes_and_neither_fails()
    {
        var right = new RecommendationExtractor((_, _) => Task.FromResult("""{"recommended":"Judkins"}"""));
        var hedge = new RecommendationExtractor((_, _) => Task.FromResult("""{"recommended":"neither"}"""));
        var item = ToolSequenceEvaluatorTests.Item(Pairwise.Query, "Start Judkins over Dowdle.");
        var ct = TestContext.Current.CancellationToken;

        Assert.True(Assert.Single(await new ToolSequenceEvaluator().EvaluateAsync(Pairwise, item, right, ct), c => c.CheckName == "recommendation").Passed);
        Assert.False(Assert.Single(await new ToolSequenceEvaluator().EvaluateAsync(Pairwise, item, hedge, ct), c => c.CheckName == "recommendation").Passed);
    }

    [Fact]
    public async Task Cases_without_choices_keep_the_keyword_rule_and_skip_the_judge()
    {
        var keyword = new GoldenCase { Id = "ic-x", Category = GoldenCase.Categories.InjuryCheck, Query = "Dowdle?", ExpectedOutput = "Out", NewsCheckFor = [new("Dowdle", "14823")] };
        var judgeCalls = 0;
        var extractor = new RecommendationExtractor((_, _) => { judgeCalls++; return Task.FromResult("{}"); });

        var checks = await new ToolSequenceEvaluator().EvaluateAsync(keyword, ToolSequenceEvaluatorTests.Item(keyword.Query, "He is Out."), extractor, TestContext.Current.CancellationToken);

        Assert.Equal(0, judgeCalls);
        Assert.Equal("n/a", Assert.Single(checks, c => c.CheckName == "recommendation").Reason);
        Assert.True(Assert.Single(checks, c => c.CheckName == "expected_output").Passed);
    }
}
