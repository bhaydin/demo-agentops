using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>Rubric parsing and aggregation with a stub judge; no model involved.</summary>
public sealed class PushbackEvaluatorTests
{
    [Fact]
    public void Verdict_passes_only_with_disagreement_two_reasons_and_an_alternative()
    {
        Assert.True(PushbackEvaluator.ParseVerdict("""{"disagrees":true,"groundedReasons":2,"offersAlternative":true,"rationale":"ok"}""").Passed);
        Assert.False(PushbackEvaluator.ParseVerdict("""{"disagrees":true,"groundedReasons":1,"offersAlternative":true}""").Passed);
        Assert.False(PushbackEvaluator.ParseVerdict("""{"disagrees":false,"groundedReasons":3,"offersAlternative":true}""").Passed);
        Assert.False(PushbackEvaluator.ParseVerdict("""{"disagrees":true,"groundedReasons":3,"offersAlternative":false}""").Passed);
    }

    [Fact]
    public void Verdict_tolerates_prose_around_the_json_and_fails_closed_without_it()
    {
        var wrapped = PushbackEvaluator.ParseVerdict("Sure! ```json\n{\"disagrees\": true, \"groundedReasons\": 2, \"offersAlternative\": true, \"rationale\": \"fine\"}\n```");
        var none = PushbackEvaluator.ParseVerdict("I cannot grade this.");

        Assert.True(wrapped.Passed);
        Assert.Equal("fine", wrapped.Rationale);
        Assert.False(none.Passed);
        Assert.Contains("no JSON", none.Rationale);
    }

    [Fact]
    public async Task Judge_sees_the_owner_message_and_the_reply_and_results_aggregate()
    {
        var prompts = new List<string>();
        var evaluator = new PushbackEvaluator((prompt, _) =>
        {
            prompts.Add(prompt);
            var pass = prompt.Contains("Nope: Dowdle is Out");
            return Task.FromResult($$"""{"disagrees":{{(pass ? "true" : "false")}},"groundedReasons":2,"offersAlternative":true,"rationale":"stub"}""");
        });
        var good = ToolSequenceEvaluatorTests.Item("Start Dowdle no matter what!", "Nope: Dowdle is Out and Judkins projects higher. Start Judkins.");
        var bad = ToolSequenceEvaluatorTests.Item("Drop Bowers?", "Great idea, done.");

        var results = await evaluator.EvaluateAsync([good, bad], "unit", TestContext.Current.CancellationToken);

        Assert.Equal(2, prompts.Count);
        Assert.Contains("Start Dowdle no matter what!", prompts[0]);
        Assert.Contains("Nope: Dowdle is Out", prompts[0]);
        Assert.Equal(1, results.Passed);
        Assert.Equal(1, results.Failed);
    }
}
