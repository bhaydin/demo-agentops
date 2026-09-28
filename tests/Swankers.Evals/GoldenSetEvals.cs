using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>
/// The Phase 5 gate: runs the golden set against the Coach for one prompt version and fails
/// when a category drops below its threshold. Needs Foundry model access (FOUNDRY_PROJECT_ENDPOINT
/// and an Azure identity); without it the test is skipped so build.yml stays model-free.
/// </summary>
public sealed class GoldenSetEvals(ITestOutputHelper output)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Coach_meets_the_golden_set_thresholds()
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), "FOUNDRY_PROJECT_ENDPOINT is not set; model-backed evals run in evals.yml or locally after azd up.");

        var settings = EvalSettings.Load();
        var version = settings.EffectivePromptVersion;
        var model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") is { Length: > 0 } m ? m : settings.JudgeModel;
        var cases = GoldenSet.Load();
        output.WriteLine($"Coach {version}: {cases.Count} cases, model {model}, judge {settings.JudgeModel}.");

        await using var coach = await CoachUnderTest.StartAsync(version, endpoint!, model, CT);
        var sequence = new ToolSequenceEvaluator();
        var pushback = new PushbackEvaluator(coach.CreateJudge(settings.JudgeModel));

        var results = new List<CaseResult>();
        var items = new List<(GoldenCase Case, EvalItem Item)>();
        foreach (var golden in cases)
        {
            for (var repetition = 0; repetition < Math.Max(1, settings.Repetitions); repetition++)
            {
                var result = await RunCaseAsync(coach, golden, settings, sequence, pushback, items);
                results.Add(result);
                output.WriteLine($"{result.Case.Id}: {(result.Passed ? "PASS" : "FAIL " + string.Join("; ", result.Failures))}");
            }
        }

        var foundry = settings.Foundry.Enabled ? await RunFoundryAsync(coach, settings, version, items) : null;

        var report = new EvalReport(version, settings, results, foundry);
        var path = report.Write(CoachUnderTest.RepoRoot);
        output.WriteLine(report.ToMarkdown());
        output.WriteLine($"Report: {path}");

        Assert.True(report.Passed, "Gate failed:\n" + string.Join("\n", report.GateFailures));
    }

    private static async Task<CaseResult> RunCaseAsync(
        CoachUnderTest coach, GoldenCase golden, EvalSettings settings, ToolSequenceEvaluator sequence, PushbackEvaluator pushback,
        List<(GoldenCase, EvalItem)> items)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.CaseTimeoutSeconds));
        try
        {
            await coach.PrepareAsync(golden, timeout.Token);
            var response = await coach.RunAsync(golden, timeout.Token);

            var conversation = new List<ChatMessage> { new(ChatRole.User, golden.Query) };
            conversation.AddRange(response.Messages);
            var item = new EvalItem(golden.Query, response.Text, conversation)
            {
                Tools = coach.Tools,
                ExpectedOutput = golden.ExpectedOutput,
            };
            items.Add((golden, item));

            var checks = sequence.Evaluate(golden, item);
            var verdict = golden.Category == GoldenCase.Categories.Pushback
                ? await pushback.JudgeAsync(golden.Query, response.Text, timeout.Token)
                : null;
            return new CaseResult(golden, response.Text, ToolCall.From(item), checks, verdict, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !CT.IsCancellationRequested)
        {
            var error = ex is OperationCanceledException ? $"timed out after {settings.CaseTimeoutSeconds}s" : $"{ex.GetType().Name}: {ex.Message}";
            return new CaseResult(golden, "", [], [], null, error);
        }
    }

    /// <summary>Cloud evaluators over the same items; a service error is reported, not thrown.</summary>
    private static async Task<FoundrySummary> RunFoundryAsync(CoachUnderTest coach, EvalSettings settings, string version, List<(GoldenCase Case, EvalItem Item)> items)
    {
        if (items.Count == 0)
        {
            return new FoundrySummary("skipped", null, new Dictionary<string, (int, int)>(), "no items");
        }

        try
        {
            var evals = new FoundryEvals(coach.Project, settings.JudgeModel, settings.Foundry.Evaluators.ToArray());
            var results = await evals.EvaluateAsync(items.Select(i => i.Item).ToList(), $"swankers-coach-{version}", CT);
            var perEvaluator = new Dictionary<string, (int Passed, int Failed)>();
            if (results.PerEvaluator is not null)
            {
                foreach (var (name, summary) in results.PerEvaluator)
                {
                    perEvaluator[name] = (summary.Passed, summary.Failed);
                }
            }

            return new FoundrySummary(results.Status, results.ReportUrl, perEvaluator, results.Error);
        }
        catch (Exception ex)
        {
            return new FoundrySummary("error", null, new Dictionary<string, (int, int)>(), $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
