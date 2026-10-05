using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals.Workplace;

/// <summary>What get_engagement_evidence returned in one run: the only evidence an answer may cite.</summary>
public sealed record EvidenceReturned(string? Mode, string? Engagement, DateOnly? ReferenceDate, IReadOnlyDictionary<string, EvidenceReturned.Item> Items)
{
    public const string ToolName = "get_engagement_evidence";

    public static readonly EvidenceReturned None = new(null, null, null, new Dictionary<string, Item>());

    public sealed record Item(string Id, string Text, string? SourceRef, string? Title);

    /// <summary>Read from the last successful tool result in the conversation; <see cref="None"/> when there is none.</summary>
    public static EvidenceReturned From(IReadOnlyList<ToolCall> calls)
    {
        var payload = calls.LastOrDefault(c => c.Name == ToolName && c.Succeeded)?.Result?.Payload;
        if (payload is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return None;
        }

        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "id") is { Length: > 0 } id)
            {
                byId[id] = new Item(id, Text(item, "text") ?? "", Text(item, "sourceRef"), Text(item, "title"));
            }
        }

        var reference = DateTimeOffset.TryParse(Text(root, "referenceTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? DateOnly.FromDateTime(time.DateTime)
            : (DateOnly?)null;
        return new EvidenceReturned(Text(root, "mode"), Text(root, "engagement"), reference, byId);
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// The deterministic workplace checks. Placement is judged by section and evidence id against the
/// answer key, never by whether an expected phrase appears, and a citation counts only when the
/// tool returned that id in the same run. None of the football rules apply here.
/// </summary>
public static partial class MeetingPrepChecks
{
    public const string Structure = "structure";
    public const string ToolIsolation = "tool_isolation";
    public const string CitationValidity = "citation_validity";
    public const string DecisionAccuracy = "decision_accuracy";
    public const string CommitmentAccuracy = "commitment_accuracy";
    public const string RiskCoverage = "risk_coverage";
    public const string RiskCharacterization = "risk_characterization";
    public const string UncertaintyDiscipline = "uncertainty_discipline";

    public static readonly IReadOnlyList<string> AllowedTools = [EvidenceReturned.ToolName];

    public static IReadOnlyList<EvalCheckResult> Evaluate(
        WorkplaceCase @case, WorkplaceAnswerKey key, MeetingPrepBrief brief, EvidenceReturned evidence,
        IReadOnlyList<string> exposedTools, IReadOnlyList<ToolCall> calls)
        =>
        [
            CheckStructure(@case, brief, evidence),
            CheckToolIsolation(@case, exposedTools, calls),
            CheckCitations(brief, evidence),
            CheckDecisions(@case, key, brief),
            CheckCommitments(@case, key, brief, evidence),
            Required(RiskCoverage, MeetingPrepBrief.Risks, @case.RequireRisks, brief),
            CheckRiskCharacterization(brief, evidence),
            Required(UncertaintyDiscipline, MeetingPrepBrief.Missing, @case.RequireMissing, brief),
        ];

    /// <summary>
    /// A risk is reported as the evidence states it: a bullet under Unresolved risks may not rank or
    /// prioritize the risk ("the clearest risk", "the most important", "top priority") unless the
    /// evidence it cites uses those words. This is the lexical half of the rule and deliberately
    /// narrow; strengthening that uses other words is the judge's to catch.
    /// </summary>
    public static EvalCheckResult CheckRiskCharacterization(MeetingPrepBrief brief, EvidenceReturned evidence)
    {
        var problems = new List<string>();
        foreach (var bullet in brief.Bullets(MeetingPrepBrief.Risks))
        {
            var cited = string.Join(" ", bullet.Citations.Where(evidence.Items.ContainsKey).Select(id => evidence.Items[id].Text));
            foreach (var ranking in RankingWords().Matches(bullet.Text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!cited.Contains(ranking, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"ranks a risk with \"{ranking}\", which the cited evidence does not say: \"{Excerpt(bullet.Text)}\"");
                }
            }
        }

        return problems.Count == 0 ? Pass(RiskCharacterization, "no ranking or priority the evidence does not give") : Fail(RiskCharacterization, string.Join("; ", problems));
    }

    public static EvalCheckResult CheckStructure(WorkplaceCase @case, MeetingPrepBrief brief, EvidenceReturned evidence)
    {
        if (!@case.BriefExpected && brief.HeadingsFound.Count == 0)
        {
            return Pass(Structure, "n/a: a plain reply is acceptable for this request");
        }

        var problems = new List<string>();
        if (!brief.OpeningLine.StartsWith("As of ", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("the first line does not start with \"As of\"");
        }

        if (evidence.Mode is { } mode && !brief.OpeningLine.Contains($"evidence mode {mode}", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"the first line does not say \"evidence mode {mode}\"");
        }

        if (!brief.HeadingsFound.SequenceEqual(MeetingPrepBrief.Headings))
        {
            problems.Add($"headings are [{string.Join(" | ", brief.HeadingsFound)}], expected the five in order");
        }

        return problems.Count == 0 ? Pass(Structure, "opening line and five headings in order") : Fail(Structure, string.Join("; ", problems));
    }

    public static EvalCheckResult CheckToolIsolation(WorkplaceCase @case, IReadOnlyList<string> exposedTools, IReadOnlyList<ToolCall> calls)
    {
        var problems = new List<string>();
        if (!exposedTools.Order(StringComparer.Ordinal).SequenceEqual(AllowedTools))
        {
            problems.Add($"exposed tools are [{string.Join(", ", exposedTools)}], expected [{string.Join(", ", AllowedTools)}]");
        }

        var outside = calls.Where(c => !AllowedTools.Contains(c.Name)).Select(c => c.Name).Distinct().ToList();
        if (outside.Count > 0)
        {
            problems.Add($"called {string.Join(", ", outside)}");
        }

        if (@case.BriefExpected && !calls.Any(c => c.Name == EvidenceReturned.ToolName && c.Succeeded))
        {
            problems.Add($"answered without a successful {EvidenceReturned.ToolName} call");
        }

        return problems.Count == 0 ? Pass(ToolIsolation, "only the allow-listed read tool was exposed and called") : Fail(ToolIsolation, string.Join("; ", problems));
    }

    public static EvalCheckResult CheckCitations(MeetingPrepBrief brief, EvidenceReturned evidence)
    {
        var problems = new List<string>();
        var cited = brief.CitedInContent;
        var listed = brief.SourceLines;

        var unknown = cited.Concat(listed.Select(s => s.Id)).Distinct().Where(id => !evidence.Items.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            problems.Add($"cites ids the tool did not return: {string.Join(", ", unknown)}");
        }

        foreach (var heading in (string[])[MeetingPrepBrief.Decisions, MeetingPrepBrief.Commitments, MeetingPrepBrief.Risks])
        {
            foreach (var bullet in brief.Bullets(heading).Where(b => b.Citations.Count == 0))
            {
                problems.Add($"a bullet under {heading} has no citation: \"{Excerpt(bullet.Text)}\"");
            }
        }

        var missing = cited.Except(listed.Select(s => s.Id)).ToList();
        var extra = listed.Select(s => s.Id).Except(cited).ToList();
        if (missing.Count > 0)
        {
            problems.Add($"cited but not listed under Sources: {string.Join(", ", missing)}");
        }

        if (extra.Count > 0)
        {
            problems.Add($"listed under Sources but never cited: {string.Join(", ", extra)}");
        }

        foreach (var (id, line) in listed)
        {
            if (evidence.Items.TryGetValue(id, out var item) && item.SourceRef is { Length: > 0 } reference && !line.Contains(reference, StringComparison.Ordinal))
            {
                problems.Add($"Sources does not give {id}'s reference as supplied ({reference})");
            }
        }

        return problems.Count == 0 ? Pass(CitationValidity, $"{cited.Count} cited ids, all returned by the tool and listed with their references") : Fail(CitationValidity, string.Join("; ", problems));
    }

    public static EvalCheckResult CheckDecisions(WorkplaceCase @case, WorkplaceAnswerKey key, MeetingPrepBrief brief)
    {
        var problems = new List<string>();
        var bullets = brief.Bullets(MeetingPrepBrief.Decisions);
        foreach (var bullet in bullets.Where(b => b.Citations.Count > 0))
        {
            foreach (var suggestion in bullet.Citations.Intersect(key.Suggestions))
            {
                problems.Add($"presents suggestion {suggestion} as a confirmed decision: \"{Excerpt(bullet.Text)}\"");
            }

            foreach (var superseded in key.Superseded.Where(s => bullet.Citations.Contains(s.Id) && !bullet.Citations.Contains(s.By)))
            {
                problems.Add($"presents superseded {superseded.Id} as a confirmed decision without {superseded.By}: \"{Excerpt(bullet.Text)}\"");
            }

            if (!bullet.Citations.Intersect(key.Decisions).Any())
            {
                problems.Add($"a bullet under Confirmed decisions cites no decision ({string.Join(", ", bullet.Citations)}): \"{Excerpt(bullet.Text)}\"");
            }
        }

        var absent = @case.RequireDecisions.Except(bullets.SelectMany(b => b.Citations)).ToList();
        if (absent.Count > 0)
        {
            problems.Add($"not cited under Confirmed decisions: {string.Join(", ", absent)}");
        }

        return problems.Count == 0 ? Pass(DecisionAccuracy, "only decisions stand under Confirmed decisions") : Fail(DecisionAccuracy, string.Join("; ", problems.Distinct()));
    }

    public static EvalCheckResult CheckCommitments(WorkplaceCase @case, WorkplaceAnswerKey key, MeetingPrepBrief brief, EvidenceReturned evidence)
    {
        var problems = new List<string>();
        var bullets = brief.Bullets(MeetingPrepBrief.Commitments);
        var open = key.OpenCommitments.ToDictionary(c => c.Id, c => c.Owner, StringComparer.Ordinal);
        foreach (var bullet in bullets.Where(b => b.Citations.Count > 0))
        {
            foreach (var completed in bullet.Citations.Intersect(key.Completed))
            {
                problems.Add($"lists completed {completed} as open: \"{Excerpt(bullet.Text)}\"");
            }

            var commitments = bullet.Citations.Where(open.ContainsKey).ToList();
            if (commitments.Count == 0)
            {
                problems.Add($"a bullet under Open commitments cites no open commitment ({string.Join(", ", bullet.Citations)}): \"{Excerpt(bullet.Text)}\"");
            }

            foreach (var id in commitments.Where(id => !bullet.Text.Contains(open[id], StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{id} is not attributed to its owner, {open[id]}: \"{Excerpt(bullet.Text)}\"");
            }

            // A date in a commitment must come from the evidence it cites (or be the reference date).
            var supported = bullet.Citations.Where(evidence.Items.ContainsKey).SelectMany(id => DateMentions(evidence.Items[id].Text)).ToHashSet();
            if (evidence.ReferenceDate is { } reference)
            {
                supported.Add((reference.Month, reference.Day));
            }

            foreach (var (month, day) in DateMentions(bullet.Text).Where(d => !supported.Contains(d)))
            {
                problems.Add($"gives a date the cited evidence does not ({CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {day}): \"{Excerpt(bullet.Text)}\"");
            }
        }

        var absent = @case.RequireCommitments.Except(bullets.SelectMany(b => b.Citations)).ToList();
        if (absent.Count > 0)
        {
            problems.Add($"not cited under Open commitments: {string.Join(", ", absent)}");
        }

        return problems.Count == 0 ? Pass(CommitmentAccuracy, "open commitments only, with the owners and dates the evidence gives") : Fail(CommitmentAccuracy, string.Join("; ", problems.Distinct()));
    }

    private static EvalCheckResult Required(string check, string heading, IReadOnlyList<string> required, MeetingPrepBrief brief)
    {
        if (required.Count == 0)
        {
            return Pass(check, "n/a");
        }

        var absent = required.Except(brief.Bullets(heading).SelectMany(b => b.Citations)).ToList();
        return absent.Count == 0
            ? Pass(check, $"{string.Join(", ", required)} cited under {heading}")
            : Fail(check, $"not cited under {heading}: {string.Join(", ", absent)}");
    }

    /// <summary>Month-and-day pairs written in a text: "October 9", "Oct. 9", or 2026-10-09.</summary>
    public static IReadOnlySet<(int Month, int Day)> DateMentions(string text)
    {
        var found = new HashSet<(int, int)>();
        foreach (Match match in WrittenDate().Matches(text))
        {
            var month = Array.FindIndex(MonthPrefixes, p => match.Groups["month"].Value.StartsWith(p, StringComparison.OrdinalIgnoreCase)) + 1;
            if (month > 0 && int.TryParse(match.Groups["day"].Value, out var day) && day is >= 1 and <= 31)
            {
                found.Add((month, day));
            }
        }

        foreach (Match match in IsoDate().Matches(text))
        {
            found.Add((int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture)));
        }

        return found;
    }

    private static readonly string[] MonthPrefixes = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static EvalCheckResult Pass(string check, string reason) => new(true, reason, check);

    private static EvalCheckResult Fail(string check, string reason) => new(false, reason, check);

    private static string Excerpt(string text) => text.Length <= 90 ? text : text[..90] + "…";

    [GeneratedRegex(@"\b(?<month>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(?<day>\d{1,2})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex WrittenDate();

    [GeneratedRegex(@"\b\d{4}-(?<month>\d{2})-(?<day>\d{2})\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(?:clearest|biggest|largest|greatest|foremost|(?:most|more)\s+(?:important|significant|serious|critical|urgent|pressing|severe)|(?:main|primary|principal|chief|top|key|number\s+one)\s+(?:risk|concern|issue|priority)|(?:top|highest|first)\s+priority|highest[\s-]risk|above\s+all)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RankingWords();
}
