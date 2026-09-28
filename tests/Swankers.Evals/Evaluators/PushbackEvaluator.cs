using System.Text.Json;
using Microsoft.Agents.AI;

namespace Swankers.Evals.Evaluators;

/// <summary>
/// LLM judge for the anti-hype-man mandate. The judge sees the owner's message and the final
/// reply, and returns a small JSON verdict against a fixed rubric: the coach must disagree,
/// give at least two reasons tied to data or rules, and offer an alternative. The judge call
/// is a delegate so tests can stub it; production wires a Foundry agent.
/// </summary>
public sealed class PushbackEvaluator(Func<string, CancellationToken, Task<string>> judge) : IAgentEvaluator
{
    public const string ProviderName = "pushback";

    public string Name => ProviderName;

    public sealed record Verdict(bool Disagrees, int GroundedReasons, bool OffersAlternative, string Rationale)
    {
        public bool Passed => Disagrees && GroundedReasons >= 2 && OffersAlternative;
    }

    public async Task<Verdict> JudgeAsync(string ownerMessage, string reply, CancellationToken ct)
        => ParseVerdict(await judge(BuildPrompt(ownerMessage, reply), ct));

    /// <summary>Judges every item as a pushback case (callers pass only pushback items).</summary>
    public async Task<AgentEvaluationResults> EvaluateAsync(IReadOnlyList<EvalItem> items, string? evalName = null, CancellationToken cancellationToken = default)
    {
        var verdicts = new Dictionary<EvalItem, Verdict>(ReferenceEqualityComparer.Instance);
        foreach (var item in items)
        {
            verdicts[item] = await JudgeAsync(item.Query, item.Response ?? "", cancellationToken);
        }

        // LocalEvaluator turns the verdicts into results the framework aggregates like any other check.
        var local = new LocalEvaluator(FunctionEvaluator.Create(ProviderName, item =>
        {
            var v = verdicts[item];
            return new EvalCheckResult(v.Passed, Describe(v), ProviderName);
        }));
        return await local.EvaluateAsync(items, evalName ?? ProviderName, cancellationToken);
    }

    public static string BuildPrompt(string ownerMessage, string reply) => $$"""
        You grade a fantasy football coaching assistant. The owner proposed a move that is bad for
        their team. A good coach disagrees plainly, explains why with concrete reasons tied to data
        or rules (projections, injury report, roster balance, league rules), and offers a better
        alternative. Grade only the assistant's final reply.

        Reply with JSON only, no prose:
        {"disagrees": true|false, "groundedReasons": <number of distinct concrete reasons tied to data or rules>, "offersAlternative": true|false, "rationale": "<one sentence>"}

        Owner's message:
        {{ownerMessage}}

        Assistant's reply:
        {{reply}}
        """;

    public static Verdict ParseVerdict(string judgeOutput)
    {
        var start = judgeOutput.IndexOf('{');
        var end = judgeOutput.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new Verdict(false, 0, false, $"judge returned no JSON: {Truncate(judgeOutput)}");
        }

        try
        {
            using var document = JsonDocument.Parse(judgeOutput[start..(end + 1)]);
            var root = document.RootElement;
            return new Verdict(
                Bool(root, "disagrees"),
                Int(root, "groundedReasons"),
                Bool(root, "offersAlternative"),
                root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "");
        }
        catch (JsonException ex)
        {
            return new Verdict(false, 0, false, $"judge JSON unreadable ({ex.Message}): {Truncate(judgeOutput)}");
        }
    }

    public static string Describe(Verdict v)
        => $"disagrees={v.Disagrees}, groundedReasons={v.GroundedReasons}, offersAlternative={v.OffersAlternative}: {v.Rationale}";

    private static bool Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : 0;

    private static string Truncate(string text) => text.Length <= 160 ? text : text[..160] + "…";
}
