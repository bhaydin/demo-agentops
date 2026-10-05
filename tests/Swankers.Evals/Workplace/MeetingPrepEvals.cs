using System.Diagnostics;
using Microsoft.Extensions.AI;
using Swankers.Coach.Workplace;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals.Workplace;

/// <summary>
/// The WorkplaceMeetingPrep gate: runs the workplace cases against the agent for one prompt
/// version over the fixed Replay fixture, and fails unless the run passes. Opt-in with
/// WORKPLACE_EVAL=1 as well as FOUNDRY_PROJECT_ENDPOINT, so the football eval job, which sets only
/// the endpoint, never starts it. A skip is a skip, not a pass. Not offline: it calls the model
/// for the agent and for the judge.
///
///   WORKPLACE_EVAL=1            opt in
///   FOUNDRY_PROJECT_ENDPOINT    the Foundry project
///   WORKPLACE_PROMPT_VERSION    meetingprep version to evaluate (default: workplace-evalsettings.json)
///   WORKPLACE_EVAL_MODEL        agent model deployment (default: workplace-evalsettings.json)
///   WORKPLACE_EVAL_RUN          a label for this run in the report name, for example run1
/// </summary>
public sealed class MeetingPrepEvals(ITestOutputHelper output)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MeetingPrep_meets_the_workplace_thresholds()
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("WORKPLACE_EVAL") == "1" && !string.IsNullOrWhiteSpace(endpoint),
            "Opt-in model-backed evaluation: set WORKPLACE_EVAL=1 and FOUNDRY_PROJECT_ENDPOINT.");

        var settings = WorkplaceEvalSettings.Load();
        var key = WorkplaceCaseSet.LoadKey();
        var cases = WorkplaceCaseSet.LoadCases();
        WorkplaceCaseSet.Validate(cases, key);
        var version = settings.EffectivePromptVersion;
        var runLabel = Environment.GetEnvironmentVariable("WORKPLACE_EVAL_RUN") is { Length: > 0 } label ? label : "run";

        await using var sut = await MeetingPrepUnderTest.StartAsync(version, endpoint!, settings.EffectiveAgentModel, CT);
        Assert.True(sut.Fixture.Fixture == key.Fixture, $"The answer key is for {key.Fixture}, but the fixture is {sut.Fixture.Fixture}.");

        var judge = new MeetingPrepJudge(sut.CreateJudge(settings.JudgeModel));
        var repoRoot = MeetingPrepUnderTest.RepoRoot;
        var (commit, dirty) = WorkplaceProvenance.Git(repoRoot);
        var provenance = new WorkplaceProvenance(
            Scenario: "workplace",
            Mode: sut.Composition.Mode,
            RunLabel: runLabel,
            TimestampUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            GitCommit: commit,
            GitDirty: dirty,
            PromptId: sut.Composition.PromptId,
            PromptSha256: sut.Composition.PromptSha256,
            Fixture: sut.Fixture.Fixture,
            FixtureSha256: sut.Fixture.Sha256,
            CasesSha256: WorkplaceCaseSet.Sha256(Path.Combine(WorkplaceCaseSet.DefaultDirectory, WorkplaceCaseSet.CasesFile)),
            AnswerKeySha256: WorkplaceCaseSet.Sha256(Path.Combine(WorkplaceCaseSet.DefaultDirectory, WorkplaceCaseSet.KeyFile)),
            SettingsSha256: WorkplaceCaseSet.Sha256(WorkplaceEvalSettings.DefaultPath),
            ProjectEndpoint: endpoint!,
            AgentModel: sut.AgentModel,
            JudgeModel: settings.JudgeModel,
            JudgeConfig: judge.Config.Version,
            JudgeConfigSha256: judge.Config.Sha256,
            Defect: "none",
            TraceIds: "not exported");
        output.WriteLine($"{provenance.PromptId} {runLabel}: {cases.Count} cases, mode {provenance.Mode}, fixture {provenance.Fixture}, agent {provenance.AgentModel}, judge {provenance.JudgeModel} ({provenance.JudgeConfig}), code {commit}{(dirty == true ? " (uncommitted changes)" : "")}.");

        var results = new List<WorkplaceCaseResult>();
        foreach (var @case in cases)
        {
            var result = await RunCaseAsync(sut, @case, key, judge, settings);
            results.Add(result);
            output.WriteLine($"{@case.Id}: {(result.Incomplete ? "INCOMPLETE" : result.Passed ? "PASS" : "FAIL")}{(result.Passed ? "" : " " + string.Join("; ", result.Failures))}");
        }

        var report = new WorkplaceEvalReport(provenance, settings, results);
        var path = report.Write(repoRoot);
        output.WriteLine(report.ToMarkdown());
        output.WriteLine($"Report: {path}");

        Assert.True(report.Status == WorkplaceEvalReport.Pass,
            $"Workplace gate {report.Status}:\n" + string.Join("\n", report.IncompleteReasons.Concat(report.GateFailures)));
    }

    private static async Task<WorkplaceCaseResult> RunCaseAsync(
        MeetingPrepUnderTest sut, WorkplaceCase @case, WorkplaceAnswerKey key, MeetingPrepJudge judge, WorkplaceEvalSettings settings)
    {
        var clock = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.CaseTimeoutSeconds));
        try
        {
            var response = await sut.RunAsync(@case, timeout.Token);
            List<ChatMessage> conversation = [new(ChatRole.User, @case.Query), .. response.Messages];
            var calls = ToolCall.From(conversation);
            var evidence = EvidenceReturned.From(calls);
            var brief = MeetingPrepBrief.Parse(response.Text);

            var checks = MeetingPrepChecks.Evaluate(@case, key, brief, evidence, sut.ExposedTools, calls);
            var judged = new[]
            {
                await judge.GroundednessAsync(brief, evidence, timeout.Token),
                await judge.ClaimsAsync(@case, response.Text, timeout.Token),
            };
            return new WorkplaceCaseResult(@case, response.Text, [.. calls.Select(c => c.Name)], checks, judged, Error: null, clock.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !CT.IsCancellationRequested)
        {
            var error = ex is OperationCanceledException
                ? $"timed out after {settings.CaseTimeoutSeconds}s"
                : $"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}";
            return new WorkplaceCaseResult(@case, "", [], [], [], error, clock.Elapsed.TotalSeconds);
        }
    }
}
