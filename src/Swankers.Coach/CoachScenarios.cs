namespace Swankers.Coach;

/// <summary>
/// The scenarios this host can compose, chosen once at startup by Coach:Scenario. Football is the
/// default and is everything the Summit demos use; workplace is the WI.NET meeting-prep scenario.
/// Each has its own prompts, tools, and evaluation cases, and one process runs exactly one.
/// </summary>
public static class CoachScenarios
{
    public const string Football = "football";
    public const string Workplace = "workplace";

    public static readonly IReadOnlyList<string> All = [Football, Workplace];

    /// <summary>The canonical scenario name, or an error that names the valid ones.</summary>
    public static string Parse(string? configured)
        => All.FirstOrDefault(s => s.Equals(configured?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown Coach:Scenario '{configured}'. Valid values: {string.Join(", ", All)}.");

    /// <summary>
    /// The prompt version a scenario runs when Coach:PromptVersion is not set: the football Coach
    /// keeps v1 (the hardened prompt the Summit demos and the hosted versions rely on); the
    /// workplace scenario runs meetingprep-v1.1, its accepted baseline. One shared default would
    /// start the workplace scenario on a prompt that is not its baseline.
    /// </summary>
    public static string DefaultPromptVersion(string scenario) => Parse(scenario) switch
    {
        Football => "v1",
        _ => "v1.1",
    };

    /// <summary>The configured version when one is given, otherwise the scenario's default.</summary>
    public static string PromptVersionFor(string scenario, string? configured)
        => string.IsNullOrWhiteSpace(configured) ? DefaultPromptVersion(scenario) : configured.Trim();
}
