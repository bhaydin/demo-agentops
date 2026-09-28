using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>What one case did and how it scored; the report and the gate are built from these.</summary>
public sealed record CaseResult(
    GoldenCase Case,
    string Response,
    IReadOnlyList<ToolCall> Calls,
    IReadOnlyList<EvalCheckResult> Checks,
    PushbackEvaluator.Verdict? Pushback,
    string? Error)
{
    public bool Passed => Error is null && Checks.All(c => c.Passed) && (Pushback?.Passed ?? true);

    public IEnumerable<string> Failures
        => Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}: {c.Reason}")
            .Concat(Pushback is { Passed: false } v ? [$"pushback: {PushbackEvaluator.Describe(v)}"] : [])
            .Concat(Error is null ? [] : [$"error: {Error}"]);
}

public sealed record FoundrySummary(string? Status, IReadOnlyList<Uri> ReportUrls, IReadOnlyDictionary<string, (int Passed, int Failed)> PerEvaluator, string? Error);

/// <summary>Markdown for humans (test output, CI log, artifact) and JSON for the promotion job.</summary>
public sealed class EvalReport(string promptVersion, EvalSettings settings, IReadOnlyList<CaseResult> results, FoundrySummary? foundry)
{
    public IReadOnlyDictionary<string, (int Passed, int Total, double Rate)> Categories { get; } =
        results.GroupBy(r => r.Case.Category).ToDictionary(
            g => g.Key,
            g => (g.Count(r => r.Passed), g.Count(), g.Count() == 0 ? 0 : (double)g.Count(r => r.Passed) / g.Count()));

    public IReadOnlyList<string> GateFailures { get; } = GateFailuresFor(settings, results, foundry);

    public bool Passed => GateFailures.Count == 0;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Coach {promptVersion} golden-set evals ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC)");
        sb.AppendLine();
        sb.AppendLine($"**Gate: {(Passed ? "PASS" : "FAIL")}**");
        foreach (var failure in GateFailures)
        {
            sb.AppendLine($"- {failure}");
        }

        sb.AppendLine();
        sb.AppendLine("| Category | Passed | Threshold |");
        sb.AppendLine("|---|---|---|");
        foreach (var category in GoldenCase.Categories.All)
        {
            var (passed, total, rate) = Categories.GetValueOrDefault(category);
            sb.AppendLine($"| {category} | {passed}/{total} ({rate:P0}) | {settings.Thresholds.GetValueOrDefault(category):P0} |");
        }

        if (foundry is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"## Foundry evaluators ({foundry.Status ?? "n/a"})");
            foreach (var url in foundry.ReportUrls)
            {
                sb.AppendLine($"Report: {url}");
            }

            if (foundry.Error is not null)
            {
                sb.AppendLine($"Error: {foundry.Error}");
            }

            foreach (var (name, (passed, failed)) in foundry.PerEvaluator.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"- {name}: {passed} passed, {failed} failed");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Cases");
        foreach (var r in results)
        {
            sb.AppendLine();
            sb.AppendLine($"### {r.Case.Id} ({r.Case.Category}) {(r.Passed ? "PASS" : "FAIL")}");
            sb.AppendLine($"Query: {r.Case.Query}");
            sb.AppendLine($"Tools: {(r.Calls.Count == 0 ? "none" : string.Join(" → ", r.Calls.Select(c => c.Name)))}");
            foreach (var failure in r.Failures)
            {
                sb.AppendLine($"- FAIL {failure}");
            }

            if (r.Pushback is { Passed: true } v)
            {
                sb.AppendLine($"- pushback: {PushbackEvaluator.Describe(v)}");
            }

            sb.AppendLine($"Answer: {Truncate(r.Response, 600)}");
        }

        return sb.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        promptVersion,
        passed = Passed,
        gateFailures = GateFailures,
        categories = Categories.ToDictionary(kv => kv.Key, kv => new { kv.Value.Passed, kv.Value.Total, kv.Value.Rate }),
        foundry = foundry is null ? null : new { foundry.Status, reportUrls = foundry.ReportUrls.Select(u => u.ToString()), foundry.Error, perEvaluator = foundry.PerEvaluator.ToDictionary(kv => kv.Key, kv => new { kv.Value.Passed, kv.Value.Failed }) },
        cases = results.Select(r => new { r.Case.Id, r.Case.Category, r.Passed, failures = r.Failures, tools = r.Calls.Select(c => c.Name) }),
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    /// <summary>Writes artifacts/evals/coach-{version}-{stamp}.md and .json under the repo; returns the markdown path.</summary>
    public string Write(string repoRoot)
    {
        var dir = Path.Combine(repoRoot, "artifacts", "evals");
        Directory.CreateDirectory(dir);
        var stem = Path.Combine(dir, $"coach-{promptVersion}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        File.WriteAllText(stem + ".md", ToMarkdown());
        File.WriteAllText(stem + ".json", ToJson());
        File.WriteAllText(Path.Combine(dir, $"latest-{promptVersion}.json"), ToJson());
        return stem + ".md";
    }

    private static IReadOnlyList<string> GateFailuresFor(EvalSettings settings, IReadOnlyList<CaseResult> results, FoundrySummary? foundry)
    {
        var failures = new List<string>();
        foreach (var category in GoldenCase.Categories.All)
        {
            var group = results.Where(r => r.Case.Category == category).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            var rate = (double)group.Count(r => r.Passed) / group.Count;
            var threshold = settings.Thresholds[category];
            if (rate < threshold)
            {
                var failed = group.Where(r => !r.Passed).Select(r => $"{r.Case.Id} ({string.Join("; ", r.Failures)})");
                failures.Add($"{category}: {rate:P0} passed, threshold {threshold:P0}. Failed: {string.Join(" | ", failed)}");
            }
        }

        if (foundry is not null)
        {
            if (foundry.Error is not null && settings.Foundry.Required)
            {
                failures.Add($"foundry: {foundry.Error}");
            }

            foreach (var (name, (passed, failed)) in foundry.PerEvaluator)
            {
                var total = passed + failed;
                if (total > 0 && (double)passed / total < settings.Foundry.MinPassRate)
                {
                    failures.Add($"foundry {name}: {passed}/{total} passed, minimum {settings.Foundry.MinPassRate:P0}");
                }
            }
        }

        return failures;
    }

    private static string Truncate(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
