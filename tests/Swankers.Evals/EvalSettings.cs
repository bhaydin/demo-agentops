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
        public bool Enabled { get; init; } = true;

        /// <summary>When true, a Foundry service error fails the gate; otherwise it is reported only.</summary>
        public bool Required { get; init; }

        public IReadOnlyList<string> Evaluators { get; init; } = ["task_adherence", "intent_resolution", "tool_call_accuracy"];
        public double MinPassRate { get; init; } = 0.8;
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
