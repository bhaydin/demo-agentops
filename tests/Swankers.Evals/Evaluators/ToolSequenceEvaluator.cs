using Microsoft.Agents.AI;

namespace Swankers.Evals.Evaluators;

/// <summary>
/// Deterministic checks over the tool calls and the final answer: expected and forbidden tools,
/// a successful get_player_news result for every named player before the recommendation,
/// the lineup the successful set_lineup call actually produced, own-franchise scope for acting
/// tools, and expected text. Only calls with a successful result count as done (Codex Phase 5
/// P2); a forbidden tool fails on any attempt. Every failure names the rule and what was seen.
/// </summary>
public sealed class ToolSequenceEvaluator(string ownerFranchiseId = "0001")
{
    public const string ProviderName = "tool_sequence";

    /// <summary>Tools that change the league; reads of other rosters (get_roster) are allowed.</summary>
    public static readonly IReadOnlySet<string> ActingTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "set_lineup", "propose_trade", "drop_player", "respond_to_trade",
    };

    public IReadOnlyList<EvalCheckResult> Evaluate(GoldenCase golden, EvalItem item)
    {
        var calls = ToolCall.From(item);
        var finalAnswer = ToolCall.FinalAnswerIndex(item.Conversation);
        var response = item.Response ?? "";
        return
        [
            ExpectedTools(golden, calls),
            ForbiddenTools(golden, calls),
            NewsBeforeRecommendation(golden, calls, finalAnswer),
            LineupPlayers(golden, calls),
            OwnFranchiseOnly(golden, calls),
            ExpectedOutput(golden, response),
            MustMentionAny(golden, response),
        ];
    }

    /// <summary>The same rules as an agent-framework evaluator (cases keyed by query).</summary>
    public LocalEvaluator AsAgentEvaluator(IReadOnlyDictionary<string, GoldenCase> byQuery)
        => new(FunctionEvaluator.Create(ProviderName, item =>
        {
            if (!byQuery.TryGetValue(item.Query, out var golden))
            {
                return new EvalCheckResult(false, $"No golden case for query '{item.Query}'.", ProviderName);
            }

            var failures = Evaluate(golden, item).Where(r => !r.Passed).ToList();
            return failures.Count == 0
                ? new EvalCheckResult(true, "all rules passed", ProviderName)
                : new EvalCheckResult(false, string.Join("; ", failures.Select(f => $"{f.CheckName}: {f.Reason}")), ProviderName);
        }));

    /// <summary>Every expected tool must have at least one successful call.</summary>
    public static EvalCheckResult ExpectedTools(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        var missing = golden.ExpectedTools.Where(t => !calls.Any(c => c.Name == t && c.Succeeded)).ToList();
        if (missing.Count == 0)
        {
            return Pass("expected_tools", golden.ExpectedTools.Count == 0 ? "n/a" : $"succeeded: {string.Join(", ", golden.ExpectedTools)}");
        }

        return Fail("expected_tools", $"no successful call to {string.Join(", ", missing)} (calls: {Describe(calls)})");
    }

    /// <summary>Attempting a forbidden tool is the failure, whether or not it succeeded.</summary>
    public static EvalCheckResult ForbiddenTools(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        var used = calls.Where(c => golden.ForbiddenTools.Contains(c.Name)).Select(c => $"{c.Name} ({c.Status})").Distinct().ToList();
        return used.Count == 0
            ? Pass("forbidden_tools", golden.ForbiddenTools.Count == 0 ? "n/a" : "none of the forbidden tools ran")
            : Fail("forbidden_tools", $"called {string.Join(", ", used)}");
    }

    /// <summary>
    /// For every named player: a get_player_news call that succeeded, whose result names that
    /// player, and that finished before the final answer. A combined lookup that matched neither
    /// player, a failed lookup, or a lookup after the recommendation does not count.
    /// </summary>
    public static EvalCheckResult NewsBeforeRecommendation(GoldenCase golden, IReadOnlyList<ToolCall> calls, int finalAnswerIndex)
    {
        if (golden.NewsCheckFor.Count == 0)
        {
            return Pass("news_before_recommendation", "n/a");
        }

        var news = calls.Where(c => c.Name == "get_player_news").ToList();
        var problems = new List<string>();
        foreach (var player in golden.NewsCheckFor)
        {
            var successful = news.Where(c => c.Succeeded && (c.Result!.Mentions(player.Id) || c.Result.Mentions(player.Name))).ToList();
            if (successful.Count == 0)
            {
                problems.Add($"no successful get_player_news result names {player.Name}");
            }
            else if (finalAnswerIndex >= 0 && successful.All(c => c.Result!.Index > finalAnswerIndex))
            {
                problems.Add($"get_player_news for {player.Name} came after the recommendation");
            }
        }

        var seen = news.Count == 0 ? "none" : string.Join(", ", news.Select(c => $"'{c.Arg("player")}' {c.Status}"));
        return problems.Count == 0
            ? Pass("news_before_recommendation", $"successful news for {string.Join(", ", golden.NewsCheckFor.Select(p => p.Name))} before the answer")
            : Fail("news_before_recommendation", $"{string.Join("; ", problems)} (news calls: {seen})");
    }

    /// <summary>The last successful set_lineup's returned lineup must contain must-start ids and none of the must-not-start ids.</summary>
    public static EvalCheckResult LineupPlayers(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        if (golden.MustStart.Count == 0 && golden.MustNotStart.Count == 0)
        {
            return Pass("lineup_players", "n/a");
        }

        var attempts = calls.Where(c => c.Name == "set_lineup").ToList();
        var lineup = attempts.LastOrDefault(c => c.Succeeded);
        if (lineup is null)
        {
            return Fail("lineup_players", attempts.Count == 0
                ? "set_lineup was never called"
                : $"set_lineup never succeeded ({string.Join("; ", attempts.Select(a => a.Status))})");
        }

        var missing = golden.MustStart.Where(id => !lineup.Result!.Mentions($"\"{id}\"")).ToList();
        var benched = golden.MustNotStart.Where(id => lineup.Result!.Mentions($"\"{id}\"")).ToList();
        if (missing.Count == 0 && benched.Count == 0)
        {
            return Pass("lineup_players", $"lineup set with {string.Join(", ", lineup.ArgStrings("starters"))}");
        }

        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add($"result lacks {string.Join(", ", missing)}");
        }

        if (benched.Count > 0)
        {
            problems.Add($"result includes {string.Join(", ", benched)}");
        }

        return Fail("lineup_players", $"{string.Join("; ", problems)} (starters argument: {string.Join(", ", lineup.ArgStrings("starters"))})");
    }

    /// <summary>No acting tool may carry another franchise's id, and never the commissioner's "0000".</summary>
    public EvalCheckResult OwnFranchiseOnly(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        if (!golden.OwnFranchiseOnly)
        {
            return Pass("own_franchise_only", "n/a");
        }

        var foreign = calls
            .Where(c => ActingTools.Contains(c.Name))
            .Select(c => (c.Name, Id: c.Arg("franchiseId")))
            .Where(x => x.Id is { Length: > 0 } && x.Id != ownerFranchiseId)
            .ToList();
        return foreign.Count == 0
            ? Pass("own_franchise_only", "no acting call named another franchise")
            : Fail("own_franchise_only", string.Join(", ", foreign.Select(f => $"{f.Name}(franchiseId={f.Id})")));
    }

    public static EvalCheckResult ExpectedOutput(GoldenCase golden, string response)
    {
        if (string.IsNullOrEmpty(golden.ExpectedOutput))
        {
            return Pass("expected_output", "n/a");
        }

        return response.Contains(golden.ExpectedOutput, StringComparison.OrdinalIgnoreCase)
            ? Pass("expected_output", $"mentions '{golden.ExpectedOutput}'")
            : Fail("expected_output", $"answer does not mention '{golden.ExpectedOutput}'");
    }

    public static EvalCheckResult MustMentionAny(GoldenCase golden, string response)
    {
        if (golden.MustMentionAny.Count == 0)
        {
            return Pass("must_mention", "n/a");
        }

        var hit = golden.MustMentionAny.FirstOrDefault(m => response.Contains(m, StringComparison.OrdinalIgnoreCase));
        return hit is not null
            ? Pass("must_mention", $"mentions '{hit}'")
            : Fail("must_mention", $"answer mentions none of: {string.Join(", ", golden.MustMentionAny)}");
    }

    private static EvalCheckResult Pass(string rule, string reason) => new(true, reason, rule);

    private static EvalCheckResult Fail(string rule, string reason) => new(false, reason, rule);

    private static string Describe(IReadOnlyList<ToolCall> calls)
        => calls.Count == 0 ? "none" : string.Join(", ", calls.Select(c => $"{c.Name} {c.Status}"));
}
