using Microsoft.Agents.AI;

namespace Swankers.Evals.Evaluators;

/// <summary>
/// Deterministic checks over the tool calls and the final answer: expected and forbidden tools,
/// get_player_news before any start/sit recommendation, lineup contents, own-franchise scope,
/// and expected text. Every failure names the rule and what was seen, so a red v2 run reads
/// "get_player_news was not called for Judkins", not "score 0.6".
/// </summary>
public sealed class ToolSequenceEvaluator(string ownerFranchiseId = "0001")
{
    public const string ProviderName = "tool_sequence";

    public IReadOnlyList<EvalCheckResult> Evaluate(GoldenCase golden, EvalItem item)
    {
        var calls = ToolCall.From(item);
        var response = item.Response ?? "";
        return
        [
            ExpectedTools(golden, calls),
            ForbiddenTools(golden, calls),
            NewsBeforeRecommendation(golden, calls),
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

    public static EvalCheckResult ExpectedTools(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        var missing = golden.ExpectedTools.Where(t => calls.All(c => c.Name != t)).ToList();
        return missing.Count == 0
            ? Pass("expected_tools", golden.ExpectedTools.Count == 0 ? "n/a" : $"called {string.Join(", ", golden.ExpectedTools)}")
            : Fail("expected_tools", $"never called {string.Join(", ", missing)} (calls: {Names(calls)})");
    }

    public static EvalCheckResult ForbiddenTools(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        var used = calls.Select(c => c.Name).Where(golden.ForbiddenTools.Contains).Distinct().ToList();
        return used.Count == 0
            ? Pass("forbidden_tools", golden.ForbiddenTools.Count == 0 ? "n/a" : "none of the forbidden tools ran")
            : Fail("forbidden_tools", $"called {string.Join(", ", used)}");
    }

    /// <summary>Every named player must have had get_player_news called with a matching player argument.</summary>
    public static EvalCheckResult NewsBeforeRecommendation(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        if (golden.NewsCheckFor.Count == 0)
        {
            return Pass("news_before_recommendation", "n/a");
        }

        var news = calls.Where(c => c.Name == "get_player_news").Select(c => c.Arg("player") ?? "").ToList();
        var unchecked_ = golden.NewsCheckFor
            .Where(player => !news.Any(arg => arg.Contains(player, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return unchecked_.Count == 0
            ? Pass("news_before_recommendation", $"get_player_news called for {string.Join(", ", golden.NewsCheckFor)}")
            : Fail("news_before_recommendation", $"get_player_news was not called for {string.Join(", ", unchecked_)} (news calls: {(news.Count == 0 ? "none" : string.Join(", ", news))})");
    }

    public static EvalCheckResult LineupPlayers(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        if (golden.MustStart.Count == 0 && golden.MustNotStart.Count == 0)
        {
            return Pass("lineup_players", "n/a");
        }

        var lineups = calls.Where(c => c.Name == "set_lineup").ToList();
        if (lineups.Count == 0)
        {
            return Fail("lineup_players", "set_lineup was never called");
        }

        var starters = lineups[^1].ArgStrings("starters");
        var missing = golden.MustStart.Where(id => !starters.Contains(id)).ToList();
        var benched = golden.MustNotStart.Where(starters.Contains).ToList();
        if (missing.Count == 0 && benched.Count == 0)
        {
            return Pass("lineup_players", $"starters {string.Join(", ", starters)}");
        }

        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add($"missing {string.Join(", ", missing)}");
        }

        if (benched.Count > 0)
        {
            problems.Add($"started {string.Join(", ", benched)}");
        }

        return Fail("lineup_players", $"{string.Join("; ", problems)} (starters: {string.Join(", ", starters)})");
    }

    public EvalCheckResult OwnFranchiseOnly(GoldenCase golden, IReadOnlyList<ToolCall> calls)
    {
        if (!golden.OwnFranchiseOnly)
        {
            return Pass("own_franchise_only", "n/a");
        }

        var foreign = calls
            .Select(c => (c.Name, Id: c.Arg("franchiseId")))
            .Where(x => x.Id is { Length: > 0 } && x.Id != ownerFranchiseId)
            .ToList();
        return foreign.Count == 0
            ? Pass("own_franchise_only", "no call named another franchise")
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

    private static string Names(IReadOnlyList<ToolCall> calls)
        => calls.Count == 0 ? "none" : string.Join(", ", calls.Select(c => c.Name));
}
