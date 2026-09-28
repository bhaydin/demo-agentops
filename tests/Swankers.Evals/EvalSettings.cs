using System.Text.Json;

namespace Swankers.Evals;

/// <summary>Binds evalsettings.json. Thresholds are pass rates per category (0..1).</summary>
public sealed record EvalSettings
{
    public string PromptVersion { get; init; } = "v1";
    public string JudgeModel { get; init; } = "gpt-5.4";
    public int Repetitions { get; init; } = 1;
    public int CaseTimeoutSeconds { get; init; } = 180;
    public IReadOnlyDictionary<string, double> Thresholds { get; init; } = new Dictionary<string, double>();
    public FoundrySettings Foundry { get; init; } = new();

    public sealed record FoundrySettings
    {
        /// <summary>
        /// When true, every gated evaluator must return a nonempty result for the gate to pass:
        /// a missing or errored cloud run is a gate failure, never a silent pass (Codex Phase 5 P2).
        /// </summary>
        public bool Enabled { get; init; } = true;

        public IReadOnlyList<string> Evaluators { get; init; } = ["task_adherence", "intent_resolution", "tool_call_accuracy"];

        /// <summary>Evaluators whose pass rate gates the run; the rest are run and reported only.</summary>
        public IReadOnlyList<string> Gated { get; init; } = ["task_adherence", "intent_resolution"];

        public double MinPassRate { get; init; } = 0.8;

        public bool IsGated(string evaluator) => Gated.Contains(evaluator, StringComparer.OrdinalIgnoreCase);
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "evalsettings.json");

    public static EvalSettings Load(string? path = null)
    {
        var settings = JsonSerializer.Deserialize<EvalSettings>(File.ReadAllText(path ?? DefaultPath), GoldenCase.JsonOptions)
            ?? throw new InvalidDataException("evalsettings.json is empty.");
        foreach (var category in GoldenCase.Categories.All)
        {
            if (!settings.Thresholds.ContainsKey(category))
            {
                throw new InvalidDataException($"evalsettings.json has no threshold for '{category}'.");
            }
        }

        return settings;
    }

    /// <summary>The version under test: COACH_PROMPT_VERSION wins over the file.</summary>
    public string EffectivePromptVersion
        => Environment.GetEnvironmentVariable("COACH_PROMPT_VERSION") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : PromptVersion;
}
