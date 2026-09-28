using System.Text.Json;
using System.Text.Json.Serialization;

namespace Swankers.Evals;

/// <summary>
/// One golden case. Deterministic expectations are evaluated by <see cref="Evaluators.ToolSequenceEvaluator"/>;
/// pushback cases are additionally judged by <see cref="Evaluators.PushbackEvaluator"/>.
/// </summary>
public sealed record GoldenCase
{
    public required string Id { get; init; }

    /// <summary>One of <see cref="Categories"/>.</summary>
    public required string Category { get; init; }

    public required string Query { get; init; }

    /// <summary>Demo scenario to seed before the run (data/demo/{scenario}.json), if any.</summary>
    public string? Scenario { get; init; }

    /// <summary>
    /// With <see cref="Choices"/>: the choice the answer must recommend (judged by
    /// <see cref="Evaluators.RecommendationExtractor"/>). Without: text the answer must contain.
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>The candidates the owner asked about, e.g. the two players in a start/sit question.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Tools that must have been called (any order).</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Tools that must not have been called at all.</summary>
    public IReadOnlyList<string> ForbiddenTools { get; init; } = [];

    /// <summary>Players for whom get_player_news must have been called (the Coach passes a name or an id).</summary>
    public IReadOnlyList<PlayerRef> NewsCheckFor { get; init; } = [];

    /// <summary>Player ids that must be in the set_lineup starters.</summary>
    public IReadOnlyList<string> MustStart { get; init; } = [];

    /// <summary>Player ids that must not be in the set_lineup starters.</summary>
    public IReadOnlyList<string> MustNotStart { get; init; } = [];

    /// <summary>The final answer must contain at least one of these (case-insensitive).</summary>
    public IReadOnlyList<string> MustMentionAny { get; init; } = [];

    /// <summary>No tool call may carry a franchiseId other than the owner's, and never "0000".</summary>
    public bool OwnFranchiseOnly { get; init; } = true;

    public string? Notes { get; init; }

    /// <summary>A player as the tools know them: MFL id plus a name fragment ("Judkins").</summary>
    public sealed record PlayerRef(string Name, string Id)
    {
        public bool Matches(string playerArgument)
            => playerArgument.Contains(Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(playerArgument.Trim(), Id, StringComparison.Ordinal);

        public override string ToString() => $"{Name} ({Id})";
    }

    public static class Categories
    {
        public const string StartSit = "start_sit";
        public const string InjuryCheck = "injury_check";
        public const string Pushback = "pushback";
        public const string Adversarial = "adversarial";

        public static readonly IReadOnlyList<string> All = [StartSit, InjuryCheck, Pushback, Adversarial];
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
