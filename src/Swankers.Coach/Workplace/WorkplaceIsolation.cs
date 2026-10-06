using System.Text.RegularExpressions;

namespace Swankers.Coach.Workplace;

/// <summary>
/// Keeps the workplace scenario's local state and telemetry apart from the Coach's. Applied as
/// the first thing the process does, before the agent host exists, because the host reads both
/// settings from the environment:
/// <list type="bullet">
/// <item>The local Responses store. Every local agent host shares <c>~/.agentserver</c> unless
/// AGENTSERVER_STATE_ROOT says otherwise (Azure.AI.AgentServer.Core, AgentServerStatePaths). The
/// workplace scenario gets its own root per prompt version and mode, so it cannot continue a
/// football conversation, and two prompt versions cannot continue each other's. The default
/// base is <c>~/.agentserver/scenarios</c>: on this machine only, and outside the repository,
/// because a Live answer is stored there and the checkout may sit in a synced folder.</item>
/// <item>Application Insights. A machine-wide APPLICATIONINSIGHTS_CONNECTION_STRING would export
/// every local run. The workplace scenario drops it from this process; traces go where the
/// session's OTEL_EXPORTER_OTLP_ENDPOINT points, or nowhere.</item>
/// </list>
/// The football scenario is left exactly as it was. Nothing is changed when hosted in Foundry.
/// </summary>
public static partial class WorkplaceIsolation
{
    public const string StateRootVariable = "AGENTSERVER_STATE_ROOT";
    public const string ApplicationInsightsVariable = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>Set by the Foundry hosting infrastructure; it is what FoundryEnvironment.IsHosted reads.</summary>
    public const string HostingVariable = "FOUNDRY_HOSTING_ENVIRONMENT";

    /// <summary>Configuration key for the folder that holds one state root per scenario, prompt version, and mode.</summary>
    public const string StateBaseKey = "Workplace:StateRoot";

    /// <summary>What isolation does for this process. <see cref="None"/> for every scenario but workplace.</summary>
    public sealed record Plan(string? StateRoot, bool DropApplicationInsights)
    {
        public static readonly Plan None = new(null, false);

        public bool Applies => StateRoot is not null;
    }

    public static string DefaultStateBase
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentserver", "scenarios");

    /// <summary>
    /// Reads the scenario from the command line and environment, the two places it can be set
    /// before the host loads configuration, and applies the plan to this process.
    /// </summary>
    public static Plan Apply(string[] args)
    {
        // Read the hosting variable directly. FoundryEnvironment caches every variable the first
        // time it is touched, the Application Insights connection string among them; asking it
        // here would freeze the old values before this method has changed them, and the host
        // would then configure an exporter for a connection string that is no longer there.
        var hosted = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(HostingVariable));
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        var plan = For(configuration, DefaultStateBase, hosted);
        if (plan.Applies)
        {
            Environment.SetEnvironmentVariable(StateRootVariable, plan.StateRoot);
            if (plan.DropApplicationInsights)
            {
                Environment.SetEnvironmentVariable(ApplicationInsightsVariable, null);
            }
        }

        return plan;
    }

    /// <summary>The plan for a configuration, without touching the process.</summary>
    public static Plan For(IConfiguration configuration, string defaultStateBase, bool hosted)
    {
        var scenario = configuration[$"{CoachOptions.SectionName}:{nameof(CoachOptions.Scenario)}"];
        if (hosted || !CoachScenarios.Workplace.Equals(scenario?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Plan.None;
        }

        // The same resolution Program.cs makes later, so the state root names the version that runs.
        var promptVersion = CoachScenarios.PromptVersionFor(CoachScenarios.Workplace, configuration[$"{CoachOptions.SectionName}:{nameof(CoachOptions.PromptVersion)}"]);
        var mode = configuration[$"{WorkplaceOptions.SectionName}:{nameof(WorkplaceOptions.Mode)}"] ?? "";
        var stateBase = configuration[StateBaseKey] is { Length: > 0 } configured ? configured : defaultStateBase;
        var folder = $"{CoachScenarios.Workplace}-{Safe(promptVersion)}-{Safe(mode)}";
        return new Plan(Path.GetFullPath(Path.Combine(stateBase, folder)), DropApplicationInsights: true);
    }

    private static string Safe(string value)
        => NotFileSafe().Replace(value.Trim().ToLowerInvariant(), "_") is { Length: > 0 } safe ? safe : "unset";

    [GeneratedRegex("[^a-z0-9._-]")]
    private static partial Regex NotFileSafe();
}
