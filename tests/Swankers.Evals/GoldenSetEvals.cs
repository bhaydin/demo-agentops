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
        var judge = coach.CreateJudge(settings.JudgeModel);
        var sequence = new ToolSequenceEvaluator();
        var extractor = new RecommendationExtractor(judge);
        var pushback = new PushbackEvaluator(judge);

        var results = new List<CaseResult>();
        var items = new List<(GoldenCase Case, EvalItem Item)>();
        foreach (var golden in cases)
        {
            for (var repetition = 0; repetition < Math.Max(1, settings.Repetitions); repetition++)
            {
                var result = await RunCaseAsync(coach, golden, settings, sequence, extractor, pushback, items);
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
        CoachUnderTest coach, GoldenCase golden, EvalSettings settings, ToolSequenceEvaluator sequence, RecommendationExtractor extractor,
        PushbackEvaluator pushback, List<(GoldenCase, EvalItem)> items)
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

            var checks = await sequence.EvaluateAsync(golden, item, extractor, timeout.Token);
            var verdict = golden.Category == GoldenCase.Categories.Pushback
                ? await pushback.JudgeAsync(golden.Query, response.Text, timeout.Token)
                : null;
            return new CaseResult(golden, response.Text, ToolCall.From(item), checks, verdict, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !CT.IsCancellationRequested)
        {
            var error = ex switch
            {
                OperationCanceledException => $"timed out after {settings.CaseTimeoutSeconds}s",
                // A 403 names the missing data action in the body; without it the run is undiagnosable.
                System.ClientModel.ClientResultException cre => $"{cre.GetType().Name}: {cre.Message.ReplaceLineEndings(" ")} {Body(cre)}",
                _ => $"{ex.GetType().Name}: {ex.Message}",
            };
            return new CaseResult(golden, "", [], [], null, error);
        }

        static string Body(System.ClientModel.ClientResultException exception)
        {
            var content = exception.GetRawResponse()?.Content?.ToString();
            return string.IsNullOrWhiteSpace(content) ? "" : $"body: {content.ReplaceLineEndings(" ")[..Math.Min(400, content.Length)]}";
        }
    }

    /// <summary>Evaluators that need tool definitions in the item data.</summary>
    private static readonly HashSet<string> ToolEvaluators = new(StringComparer.OrdinalIgnoreCase)
    {
        FoundryEvals.ToolCallAccuracy, FoundryEvals.ToolSelection, FoundryEvals.ToolInputAccuracy, FoundryEvals.ToolOutputUtilization, FoundryEvals.ToolCallSuccess,
    };

    /// <summary>
    /// Cloud evaluators over the same items, as two runs: the 1.22.0-preview provider sends
    /// tool_definitions whenever items carry tools but does not map that field for the
    /// non-tool evaluators, and the service rejects the run (EvalValidationFailed). So the
    /// non-tool evaluators see items without tools and the tool evaluators see items with them.
    /// A service error is reported, not thrown.
    /// </summary>
    private static async Task<FoundrySummary> RunFoundryAsync(CoachUnderTest coach, EvalSettings settings, string version, List<(GoldenCase Case, EvalItem Item)> items)
    {
        if (items.Count == 0)
        {
            return new FoundrySummary("skipped", [], new Dictionary<string, (int, int)>(), "no items");
        }

        var withTools = items.Select(i => i.Item).ToList();
        var withoutTools = items.Select(i => new EvalItem(i.Item.Query, i.Item.Response, i.Item.Conversation) { ExpectedOutput = i.Item.ExpectedOutput }).ToList();
        var runs = new List<(string[] Evaluators, IReadOnlyList<EvalItem> Items, string Name)>
        {
            (settings.Foundry.Evaluators.Where(e => !ToolEvaluators.Contains(e)).ToArray(), withoutTools, $"swankers-coach-{version}"),
            (settings.Foundry.Evaluators.Where(ToolEvaluators.Contains).ToArray(), withTools, $"swankers-coach-{version}-tools"),
        };

        var perEvaluator = new Dictionary<string, (int Passed, int Failed)>();
        var urls = new List<Uri>();
        var statuses = new List<string>();
        var errors = new List<string>();
        foreach (var (evaluators, runItems, name) in runs.Where(r => r.Evaluators.Length > 0))
        {
            try
            {
                var results = await new FoundryEvals(coach.Project, settings.JudgeModel, evaluators).EvaluateAsync(runItems, name, CT);
                statuses.Add(results.Status ?? "unknown");
                if (results.ReportUrl is not null)
                {
                    urls.Add(results.ReportUrl);
                }

                if (!string.IsNullOrWhiteSpace(results.Error))
                {
                    errors.Add(results.Error);
                }

                if (results.PerEvaluator is not null)
                {
                    foreach (var (evaluator, summary) in results.PerEvaluator)
                    {
                        perEvaluator[evaluator] = (summary.Passed, summary.Failed);
                    }
                }
            }
            catch (Exception ex)
            {
                statuses.Add("error");
                errors.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return new FoundrySummary(string.Join("+", statuses), urls, perEvaluator, errors.Count == 0 ? null : string.Join(" | ", errors));
    }
}
