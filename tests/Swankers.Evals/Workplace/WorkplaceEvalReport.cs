using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace Swankers.Evals.Workplace;

/// <summary>Binds workplace-evalsettings.json: the workplace gate, apart from the football evalsettings.json.</summary>
public sealed record WorkplaceEvalSettings
{
    public string PromptVersion { get; init; } = "v1";

    public string AgentModel { get; init; } = "gpt-5.4";

    public string JudgeModel { get; init; } = "gpt-5.4";

    public int CaseTimeoutSeconds { get; init; } = 180;

    public ThresholdSettings Thresholds { get; init; } = new();

    /// <summary>Pass rates (0..1): every deterministic check by default, and most judged checks.</summary>
    public sealed record ThresholdSettings
    {
        public double Deterministic { get; init; } = 1.0;

        public double Judged { get; init; } = 0.8;
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "workplace-evalsettings.json");

    public static WorkplaceEvalSettings Load(string? path = null)
    {
        var settings = JsonSerializer.Deserialize<WorkplaceEvalSettings>(File.ReadAllText(path ?? DefaultPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("workplace-evalsettings.json is empty.");
        if (settings.Thresholds.Deterministic is < 0 or > 1 || settings.Thresholds.Judged is < 0 or > 1)
        {
            throw new InvalidDataException("workplace-evalsettings.json thresholds must be between 0 and 1.");
        }

        return settings;
    }

    /// <summary>
    /// The version under test comes from WORKPLACE_PROMPT_VERSION and nowhere else (COACH_PROMPT_VERSION
    /// is never read here). A model-backed run must name it: a baseline, a regression, and a recovery
    /// are compared, and a comparison that silently ran on a default proves nothing. The file's
    /// <see cref="PromptVersion"/> stays as documentation of the version the settings were written for.
    /// </summary>
    public static string ExplicitPromptVersion(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? throw new InvalidOperationException("WORKPLACE_PROMPT_VERSION must be set explicitly (for example v1.1 or v2); a workplace evaluation never runs on a default prompt version.")
            : configured.Trim();

    public string EffectiveAgentModel => Environment.GetEnvironmentVariable("WORKPLACE_EVAL_MODEL") is { Length: > 0 } m ? m : AgentModel;
}

/// <summary>What one case did and how it scored.</summary>
public sealed record WorkplaceCaseResult(
    WorkplaceCase Case,
    string Answer,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<EvalCheckResult> Checks,
    IReadOnlyList<JudgedCheck> Judged,
    string? Error,
    double Seconds)
{
    /// <summary>The case errored or a judge could not be read: not a pass and not a quality finding.</summary>
    public bool Incomplete => Error is not null || Judged.Any(j => j.Outcome == JudgedOutcome.Incomplete);

    public bool Passed => !Incomplete && Checks.All(c => c.Passed) && Judged.All(j => j.Passed);

    public IEnumerable<string> Failures
        => Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}: {c.Reason}")
            .Concat(Judged.Where(j => !j.Passed).Select(j => $"{j.CheckName} ({j.Outcome.ToString().ToLowerInvariant()}): {j.Reason}"))
            .Concat(Error is null ? [] : [$"error: {Error}"]);
}

/// <summary>Everything needed to say what a result is a result of.</summary>
public sealed record WorkplaceProvenance(
    string Scenario,
    string Mode,
    string RunLabel,
    string TimestampUtc,
    string GitCommit,
    bool? GitDirty,
    string PromptId,
    string PromptSha256,
    string Fixture,
    string FixtureSha256,
    string CasesSha256,
    string AnswerKeySha256,
    string SettingsSha256,
    string ProjectEndpoint,
    string AgentModel,
    string JudgeModel,
    string JudgeConfig,
    string JudgeConfigSha256,
    string Defect,
    string TraceIds)
{
    /// <summary>The commit the run was built from, and whether the tree had uncommitted changes; "unknown" when git cannot say.</summary>
    public static (string Commit, bool? Dirty) Git(string repoRoot)
    {
        var commit = Run(repoRoot, "rev-parse HEAD");
        var status = Run(repoRoot, "status --porcelain");
        return (commit is { Length: > 0 } ? commit : "unknown", status is null ? null : status.Length > 0);

        static string? Run(string directory, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                });
                if (process is null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10_000);
                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// The workplace gate and its report. A run is pass, fail, or incomplete; incomplete (a case that
/// errored, a judge that could not be read, no cases) is never a pass. Reports go to
/// artifacts/evals/workplace/ under meetingprep- names, never to the football report names that
/// evals.yml promotes from.
/// </summary>
public sealed class WorkplaceEvalReport(WorkplaceProvenance provenance, WorkplaceEvalSettings settings, IReadOnlyList<WorkplaceCaseResult> results)
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Incomplete = "incomplete";

    public WorkplaceProvenance Provenance => provenance;

    public (int Passed, int Total) Deterministic { get; } = (results.Sum(r => r.Checks.Count(c => c.Passed)), results.Sum(r => r.Checks.Count));

    public (int Passed, int Total) Judged { get; } = (results.Sum(r => r.Judged.Count(j => j.Passed)), results.Sum(r => r.Judged.Count));

    public IReadOnlyList<string> IncompleteReasons { get; } =
    [
        .. results.Count == 0 ? ["no cases ran"] : Array.Empty<string>(),
        .. results.Where(r => r.Error is not null).Select(r => $"{r.Case.Id}: {r.Error}"),
        .. results.SelectMany(r => r.Judged.Where(j => j.Outcome == JudgedOutcome.Incomplete).Select(j => $"{r.Case.Id} {j.CheckName}: {j.Reason}")),
    ];

    public IReadOnlyList<string> GateFailures
    {
        get
        {
            var failures = new List<string>();
            if (Rate(Deterministic) < settings.Thresholds.Deterministic)
            {
                failures.Add($"deterministic checks: {Deterministic.Passed}/{Deterministic.Total} passed, threshold {Percent(settings.Thresholds.Deterministic)}");
            }

            if (Rate(Judged) < settings.Thresholds.Judged)
            {
                failures.Add($"judged checks: {Judged.Passed}/{Judged.Total} passed, threshold {Percent(settings.Thresholds.Judged)}");
            }

            return failures;
        }
    }

    public string Status => IncompleteReasons.Count > 0 ? Incomplete : GateFailures.Count > 0 ? Fail : Pass;

    public IReadOnlyDictionary<string, (int Passed, int Total)> ByCheck
        => results.SelectMany(r => r.Checks.Select(c => (Name: c.CheckName, c.Passed)).Concat(r.Judged.Select(j => (Name: j.CheckName, j.Passed))))
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => (g.Count(x => x.Passed), g.Count()));

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# WorkplaceMeetingPrep evaluation: {provenance.PromptId}, {provenance.RunLabel} ({provenance.TimestampUtc} UTC)");
        sb.AppendLine();
        sb.AppendLine($"**Gate: {Status.ToUpperInvariant()}**. Deterministic {Deterministic.Passed}/{Deterministic.Total} (threshold {Percent(settings.Thresholds.Deterministic)}); judged {Judged.Passed}/{Judged.Total} (threshold {Percent(settings.Thresholds.Judged)}); cases passed {results.Count(r => r.Passed)}/{results.Count}.");
        foreach (var line in IncompleteReasons.Select(r => $"- incomplete: {r}").Concat(GateFailures.Select(f => $"- {f}")))
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine("| Provenance | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Scenario, mode | {provenance.Scenario}, {provenance.Mode.ToUpperInvariant()} |");
        sb.AppendLine($"| Code | {provenance.GitCommit}{(provenance.GitDirty switch { true => " (uncommitted changes)", false => " (clean)", null => "" })} |");
        sb.AppendLine($"| Prompt | {provenance.PromptId}, sha256 {provenance.PromptSha256} |");
        sb.AppendLine($"| Fixture | {provenance.Fixture}, sha256 {provenance.FixtureSha256} |");
        sb.AppendLine($"| Cases, answer key, settings | sha256 {provenance.CasesSha256}, {provenance.AnswerKeySha256}, {provenance.SettingsSha256} |");
        sb.AppendLine($"| Project | {provenance.ProjectEndpoint} |");
        sb.AppendLine($"| Agent model, judge model | {provenance.AgentModel}, {provenance.JudgeModel} |");
        sb.AppendLine($"| Judge configuration | {provenance.JudgeConfig}, sha256 {provenance.JudgeConfigSha256} |");
        sb.AppendLine($"| Injected defect | {provenance.Defect} |");
        sb.AppendLine($"| Trace ids | {provenance.TraceIds} |");
        sb.AppendLine();
        sb.AppendLine("| Check | Passed |");
        sb.AppendLine("|---|---|");
        foreach (var (name, (passed, total)) in ByCheck.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {name} | {passed}/{total} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Cases");
        foreach (var r in results)
        {
            sb.AppendLine();
            sb.AppendLine($"### {r.Case.Id} {(r.Incomplete ? "INCOMPLETE" : r.Passed ? "PASS" : "FAIL")} ({r.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s)");
            sb.AppendLine($"Query: {r.Case.Query}");
            sb.AppendLine($"Tools: {(r.ToolCalls.Count == 0 ? "none" : string.Join(" → ", r.ToolCalls))}");
            foreach (var failure in r.Failures)
            {
                sb.AppendLine($"- {failure}");
            }

            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(r.Answer.Replace("```", "'''"));
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        status = Status,
        provenance,
        thresholds = settings.Thresholds,
        deterministic = new { Deterministic.Passed, Deterministic.Total },
        judged = new { Judged.Passed, Judged.Total },
        casesPassed = results.Count(r => r.Passed),
        casesTotal = results.Count,
        incompleteReasons = IncompleteReasons,
        gateFailures = GateFailures,
        byCheck = ByCheck.ToDictionary(kv => kv.Key, kv => new { kv.Value.Passed, kv.Value.Total }),
        cases = results.Select(r => new
        {
            r.Case.Id,
            r.Passed,
            r.Incomplete,
            r.Seconds,
            tools = r.ToolCalls,
            failures = r.Failures,
            checks = r.Checks.Select(c => new { name = c.CheckName, c.Passed, c.Reason }),
            judged = r.Judged.Select(j => new { name = j.CheckName, outcome = j.Outcome.ToString().ToLowerInvariant(), j.Reason, verdicts = j.Verdicts }),
            r.Error,
            r.Answer,
        }),
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    /// <summary>Where a run's files go. The names cannot collide with the football reports.</summary>
    public static (string Directory, string Stem, string Latest) Paths(string repoRoot, string promptId, string runLabel, DateTimeOffset now)
    {
        var directory = Path.Combine(repoRoot, "artifacts", "evals", "workplace");
        return (directory, Path.Combine(directory, $"{promptId}-{runLabel}-{now:yyyyMMdd-HHmmss}"), Path.Combine(directory, $"latest-{promptId}.json"));
    }

    /// <summary>Writes the markdown and JSON reports and the latest-meetingprep JSON; returns the markdown path.</summary>
    public string Write(string repoRoot)
    {
        var (directory, stem, latest) = Paths(repoRoot, provenance.PromptId, provenance.RunLabel, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        File.WriteAllText(stem + ".md", ToMarkdown());
        File.WriteAllText(stem + ".json", ToJson());
        File.WriteAllText(latest, ToJson());
        return stem + ".md";
    }

    private static double Rate((int Passed, int Total) counts) => counts.Total == 0 ? 0 : (double)counts.Passed / counts.Total;

    private static string Percent(double rate) => Math.Round(rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
