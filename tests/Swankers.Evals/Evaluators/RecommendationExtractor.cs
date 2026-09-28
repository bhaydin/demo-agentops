using System.Text.Json;

namespace Swankers.Evals.Evaluators;

/// <summary>
/// Reads which candidate the answer actually recommends starting (Codex Phase 5 P2: a name
/// merely appearing in the answer proved nothing; "Bench Judkins. Start Dowdle" mentions both).
/// The judge returns one of the case's choices, "neither", or "both"; the delegate is injected
/// so tests stub it and production wires the same Foundry judge agent as the pushback rubric.
/// </summary>
public sealed class RecommendationExtractor(Func<string, CancellationToken, Task<string>> judge)
{
    public const string Neither = "neither";
    public const string Both = "both";

    public async Task<string> ExtractAsync(string ownerMessage, string reply, IReadOnlyList<string> choices, CancellationToken ct)
        => Normalize(await judge(BuildPrompt(ownerMessage, reply, choices), ct), choices);

    public static string BuildPrompt(string ownerMessage, string reply, IReadOnlyList<string> choices) => $$"""
        The owner asked a fantasy football coach to choose between these players: {{string.Join(", ", choices)}}.
        Read the coach's final reply and report which ONE of them the reply tells the owner to START.
        If the reply recommends starting none of them, answer "neither"; if it recommends starting more than one, answer "both".

        Reply with JSON only, no prose:
        {"recommended": "<one of: {{string.Join(" | ", choices)}} | neither | both>", "rationale": "<one sentence>"}

        Owner's message:
        {{ownerMessage}}

        Coach's reply:
        {{reply}}
        """;

    /// <summary>Maps the judge's text onto a choice (case-insensitive, substring either way), else "neither".</summary>
    public static string Normalize(string judgeOutput, IReadOnlyList<string> choices)
    {
        var recommended = ReadRecommended(judgeOutput) ?? judgeOutput;
        recommended = recommended.Trim().Trim('"', '\'', '.');
        if (recommended.Equals(Both, StringComparison.OrdinalIgnoreCase))
        {
            return Both;
        }

        var matches = choices
            .Where(c => c.Equals(recommended, StringComparison.OrdinalIgnoreCase)
                || recommended.Contains(c, StringComparison.OrdinalIgnoreCase)
                || c.Contains(recommended, StringComparison.OrdinalIgnoreCase) && recommended.Length >= 3)
            .ToList();
        return matches.Count == 1 ? matches[0] : matches.Count > 1 ? Both : Neither;
    }

    private static string? ReadRecommended(string judgeOutput)
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
            return document.RootElement.TryGetProperty("recommended", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
