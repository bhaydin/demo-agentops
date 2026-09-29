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

/// <summary>
/// Cloud evaluator outcome. <paramref name="Items"/> is how many items each run was given; an
/// evaluator whose passed + failed falls short of it had grader errors or skips (CI run 36514287848: the
/// service reported "completed" while most items errored on a role-propagation delay).
/// </summary>
public sealed record FoundrySummary(string? Status, IReadOnlyList<Uri> ReportUrls, IReadOnlyDictionary<string, (int Passed, int Failed)> PerEvaluator, string? Error, int Items = 0)
{
    public int NotGraded(string evaluator)
        => PerEvaluator.TryGetValue(evaluator, out var counts) ? Math.Max(0, Items - counts.Passed - counts.Failed) : Items;
}

/// <summary>Markdown for humans (test output, CI log, artifact) and JSON for the promotion job.</summary>
public sealed class EvalReport(string promptVersion, EvalSettings settings, IReadOnlyList<CaseResult> results, FoundrySummary? foundry)
{
    public IReadOnlyDictionary<string, (int Passed, int Total, double Rate)> Categories { get; } =
        results.GroupBy(r => r.Case.Category).ToDictionary(
            g => g.Key,
            g => (g.Count(r => r.Passed), g.Count(), g.Count() == 0 ? 0 : (double)g.Count(r => r.Passed) / g.Count()));

    /// <summary>Category thresholds only: what the deterministic and judge rules said, cloud or no cloud.</summary>
    public IReadOnlyList<string> LocalGateFailures { get; } = LocalGateFailuresFor(settings, results);

    /// <summary>Local failures plus the Foundry requirements; this is what promotion reads.</summary>
    public IReadOnlyList<string> GateFailures { get; } = [.. LocalGateFailuresFor(settings, results), .. FoundryGateFailuresFor(settings, foundry)];

    public bool LocalPassed => LocalGateFailures.Count == 0;

    public bool Passed => GateFailures.Count == 0;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Coach {promptVersion} golden-set evals ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC)");
        sb.AppendLine();
        sb.AppendLine($"**Gate: {(Passed ? "PASS" : "FAIL")}**{(Passed == LocalPassed ? "" : $" (local categories: {(LocalPassed ? "PASS" : "FAIL")})")}");
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
            sb.AppendLine($"| {category} | {passed}/{total} ({Percent(rate)}) | {Percent(settings.Thresholds.GetValueOrDefault(category))} |");
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
                var notGraded = foundry.NotGraded(name) is > 0 and var n ? $", {n} of {foundry.Items} not graded" : "";
                sb.AppendLine($"- {name}: {passed} passed, {failed} failed{notGraded}{(settings.Foundry.IsGated(name) ? "" : " (report only)")}");
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
        localPassed = LocalPassed,
        gateFailures = GateFailures,
        categories = Categories.ToDictionary(kv => kv.Key, kv => new { kv.Value.Passed, kv.Value.Total, kv.Value.Rate }),
        foundry = foundry is null ? null : new { foundry.Status, reportUrls = foundry.ReportUrls.Select(u => u.ToString()), foundry.Error, foundry.Items, perEvaluator = foundry.PerEvaluator.ToDictionary(kv => kv.Key, kv => new { kv.Value.Passed, kv.Value.Failed, NotGraded = foundry.NotGraded(kv.Key) }) },
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

    private static List<string> LocalGateFailuresFor(EvalSettings settings, IReadOnlyList<CaseResult> results)
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
                failures.Add($"{category}: {Percent(rate)} passed, threshold {Percent(threshold)}. Failed: {string.Join(" | ", failed)}");
            }
        }

        return failures;
    }

    /// <summary>
    /// Every gated evaluator must have graded every item, at or above the minimum pass rate;
    /// a missing result or ungraded items (grader errors, skips) is a failure (a "completed" run can still have graded
    /// only a few items).
    /// </summary>
    private static List<string> FoundryGateFailuresFor(EvalSettings settings, FoundrySummary? foundry)
    {
        var failures = new List<string>();
        if (!settings.Foundry.Enabled)
        {
            return failures;
        }

        foreach (var name in settings.Foundry.Gated)
        {
            var (passed, failed) = foundry?.PerEvaluator
                .Where(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .FirstOrDefault() ?? (0, 0);
            var total = passed + failed;
            if (total == 0)
            {
                var why = foundry is null ? "no Foundry run" : $"status {foundry.Status ?? "unknown"}{(foundry.Error is null ? "" : $"; {foundry.Error}")}";
                failures.Add($"foundry {name}: no result ({why})");
                continue;
            }

            if (foundry!.Items > total)
            {
                failures.Add($"foundry {name}: only {total} of {foundry.Items} items graded");
                continue;
            }

            if ((double)passed / total < settings.Foundry.MinPassRate)
            {
                failures.Add($"foundry {name}: {passed}/{total} passed, minimum {Percent(settings.Foundry.MinPassRate)}");
            }
        }

        return failures;
    }

    /// <summary>"75%" on every culture; the invariant culture would format P0 as "75 %".</summary>
    private static string Percent(double rate)
        => Math.Round(rate * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string Truncate(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
