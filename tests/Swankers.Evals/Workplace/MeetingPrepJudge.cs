using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Swankers.Evals.Workplace;

/// <summary>How a judged check came out. Incomplete means the judge could not be read; it is never a pass.</summary>
public enum JudgedOutcome
{
    Pass,
    Fail,
    Incomplete,
    NotApplicable,
}

/// <summary>One judged check for one case, with the per-statement verdicts behind it.</summary>
public sealed record JudgedCheck(string CheckName, JudgedOutcome Outcome, string Reason, IReadOnlyList<JudgedCheck.Verdict> Verdicts)
{
    public sealed record Verdict(int Number, bool Pass, string Reason, string Statement);

    /// <summary>Counts toward the judged pass rate; an incomplete check does not, and makes the run incomplete.</summary>
    public bool Passed => Outcome is JudgedOutcome.Pass or JudgedOutcome.NotApplicable;
}

/// <summary>
/// The judge's configuration, versioned and hashed apart from the agent's prompt: what the judge
/// is told changes what a result means, so every report records which version graded it.
/// <para>
/// judge-v1 graded the first meetingprep-v1 baseline (commit 6a2effd). It required each specific
/// fact to be "stated in" the evidence, and failed a faithful paraphrase ("the kickoff date" for
/// "the date in the kickoff notes"). judge-v2 states the rule both ways: a faithful paraphrase
/// passes, and a statement fails only when it adds, changes, materially strengthens, or
/// contradicts a fact. Ranking a risk the evidence does not rank is named as strengthening.
/// </para>
/// </summary>
public sealed record MeetingPrepJudgeConfig(string Version, string SystemInstruction, string GroundednessInstructions, string ClaimsInstructions)
{
    public static readonly MeetingPrepJudgeConfig Current = new(
        Version: "judge-v2",
        SystemInstruction: "You are a careful grader. Apply the grading rules exactly as written. Answer with JSON only.",
        GroundednessInstructions: """
            You grade whether statements in a meeting-preparation brief are supported by the evidence they cite.
            Grade each statement only against the evidence listed under it. Use no outside knowledge and no other statement's evidence.

            Pass a statement that is a faithful account of its evidence. A paraphrase is faithful: different words, a shorter or longer phrasing, or a natural way of referring to the same thing passes when it means what the evidence says. For example, "the kickoff date" for "the date in the kickoff notes" is a faithful paraphrase. Saying that the evidence gives no date, no owner, or no mitigation passes when that is true of the evidence.

            Fail a statement only when it does at least one of these:
            - adds a fact the evidence does not state, such as a name, a date, a number, an owner, a cause, or a consequence;
            - changes a fact, such as a different owner, date, amount, or status;
            - materially strengthens what the evidence says, such as presenting a suggestion as a decision, a plan as done, or a possibility as certain, or ranking or prioritizing something (the biggest, the clearest, the most important) when the evidence gives no such ranking;
            - contradicts the evidence.

            The heading says what kind of claim the statement makes:
            - Under "Confirmed decisions" it claims the matter was decided, approved, or agreed. A proposal, a suggestion, a question, or "we should consider" does not support a decision.
            - Under "Open commitments" it claims someone committed to do it and it is not done. Evidence that it was completed does not support an open commitment.
            - Under "Unresolved risks" it claims the evidence describes that risk.

            Reply with JSON only, no prose:
            {"verdicts":[{"n":1,"pass":true,"reason":"<one sentence>"}]}
            with exactly one entry per statement, numbered as below.
            """.ReplaceLineEndings("\n"),
        ClaimsInstructions: """
            You grade an assistant's answer against a list of requirements. Judge only what the answer says, not what would be true in general.
            A requirement passes only if the answer clearly satisfies it.

            Reply with JSON only, no prose:
            {"verdicts":[{"n":1,"pass":true,"reason":"<one sentence>"}]}
            with exactly one entry per requirement, numbered as below.
            """.ReplaceLineEndings("\n"));

    /// <summary>SHA-256 over the version and every instruction text, with line endings normalized.</summary>
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n---\n", Version, SystemInstruction, GroundednessInstructions, ClaimsInstructions).ReplaceLineEndings("\n"))));
}

/// <summary>
/// The model-judged workplace checks. Groundedness asks, for every bullet in the first three
/// sections, whether the evidence it cites supports that particular claim under that heading, so
/// a tidy citation on an unsupported claim fails. Claims asks whether case-specific statements
/// about the answer hold. The judge is a delegate so tests can stub it; one call covers a case.
/// A verdict that is missing or unreadable for any statement makes the check Incomplete.
/// </summary>
public sealed class MeetingPrepJudge(Func<string, CancellationToken, Task<string>> judge, MeetingPrepJudgeConfig? config = null)
{
    public const string Groundedness = "groundedness";
    public const string Claims = "judged_claims";

    private static readonly string[] JudgedSections = [MeetingPrepBrief.Decisions, MeetingPrepBrief.Commitments, MeetingPrepBrief.Risks];

    public MeetingPrepJudgeConfig Config { get; } = config ?? MeetingPrepJudgeConfig.Current;

    public async Task<JudgedCheck> GroundednessAsync(MeetingPrepBrief brief, EvidenceReturned evidence, CancellationToken cancellationToken)
    {
        // A bullet with no valid citation is not judged here; the citation check fails it deterministically.
        var statements = JudgedSections
            .SelectMany(section => brief.Bullets(section).Select(bullet => (Section: section, Bullet: bullet)))
            .Where(s => s.Bullet.Citations.Any(evidence.Items.ContainsKey))
            .ToList();
        if (statements.Count == 0)
        {
            return new JudgedCheck(Groundedness, JudgedOutcome.NotApplicable, "no cited bullets to judge", []);
        }

        // Built with explicit line feeds so the judge reads the same prompt on every platform.
        var prompt = new StringBuilder(Config.GroundednessInstructions).Append("\n\nStatements:\n");
        for (var i = 0; i < statements.Count; i++)
        {
            prompt.Append($"{i + 1}. [{statements[i].Section}] {statements[i].Bullet.Text}\n");
            foreach (var id in statements[i].Bullet.Citations.Where(evidence.Items.ContainsKey))
            {
                prompt.Append($"   Evidence {id}: {evidence.Items[id].Text.ReplaceLineEndings(" ")}\n");
            }
        }

        return await AskAsync(Groundedness, prompt.ToString(), [.. statements.Select(s => $"[{s.Section}] {s.Bullet.Text}")], cancellationToken);
    }

    public async Task<JudgedCheck> ClaimsAsync(WorkplaceCase @case, string answer, CancellationToken cancellationToken)
    {
        if (@case.JudgedClaims.Count == 0)
        {
            return new JudgedCheck(Claims, JudgedOutcome.NotApplicable, "no judged claims for this case", []);
        }

        var prompt = new StringBuilder(Config.ClaimsInstructions)
            .Append("\n\nQuestion:\n").Append(@case.Query)
            .Append("\n\nAnswer:\n").Append(answer.ReplaceLineEndings("\n"))
            .Append("\n\nRequirements:\n");
        for (var i = 0; i < @case.JudgedClaims.Count; i++)
        {
            prompt.Append($"{i + 1}. {@case.JudgedClaims[i]}\n");
        }

        return await AskAsync(Claims, prompt.ToString(), @case.JudgedClaims, cancellationToken);
    }

    private async Task<JudgedCheck> AskAsync(string check, string prompt, IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        string output;
        try
        {
            output = await judge(prompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new JudgedCheck(check, JudgedOutcome.Incomplete, $"judge call failed: {ex.GetType().Name}: {Truncate(ex.Message)}", []);
        }

        var verdicts = ParseVerdicts(output, statements);
        if (verdicts is null)
        {
            return new JudgedCheck(check, JudgedOutcome.Incomplete, $"judge output unreadable or missing a verdict for one of {statements.Count} statements: {Truncate(output)}", []);
        }

        var failed = verdicts.Where(v => !v.Pass).ToList();
        return failed.Count == 0
            ? new JudgedCheck(check, JudgedOutcome.Pass, $"{verdicts.Count} of {verdicts.Count} hold", verdicts)
            : new JudgedCheck(check, JudgedOutcome.Fail, string.Join(" | ", failed.Select(v => $"{v.Number}. \"{Truncate(v.Statement, 80)}\": {v.Reason}")), verdicts);
    }

    /// <summary>One verdict per statement, numbered 1..n, or null when any is missing, duplicated, or unreadable.</summary>
    public static IReadOnlyList<JudgedCheck.Verdict>? ParseVerdicts(string judgeOutput, IReadOnlyList<string> statements)
    {
        var start = judgeOutput.IndexOf('{');
        var end = judgeOutput.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(judgeOutput[start..(end + 1)]);
            if (!document.RootElement.TryGetProperty("verdicts", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var byNumber = new Dictionary<int, JudgedCheck.Verdict>();
            foreach (var entry in array.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("n", out var n) || !n.TryGetInt32(out var number)
                    || !entry.TryGetProperty("pass", out var pass) || pass.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || number < 1 || number > statements.Count || byNumber.ContainsKey(number))
                {
                    return null;
                }

                var reason = entry.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                byNumber[number] = new JudgedCheck.Verdict(number, pass.ValueKind == JsonValueKind.True, reason, statements[number - 1]);
            }

            return byNumber.Count == statements.Count ? [.. byNumber.Values.OrderBy(v => v.Number)] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string text, int max = 200)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
