// Swankers.Coach: Microsoft Agent Framework agent hosted through the Foundry Responses
// protocol (AgentHost + AddFoundryResponses/MapFoundryResponses). One process hosts one scenario,
// chosen by Coach:Scenario. Football (the default): tools come from Swankers.Mcp over MCP with the
// configured credential, plus local league-knowledge search, and the stage configurations differ
// only in Coach:PromptVersion and Coach:McpCredentialKey. Workplace: one read-only evidence tool
// over a Live (Work IQ) or Replay (fixture) provider; see Workplace/WorkplaceScenario.cs.
using Azure.AI.AgentServer.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Swankers.Coach;
using Swankers.Coach.Knowledge;
using Swankers.Coach.Mcp;
using Swankers.Coach.Prompts;
using Swankers.Coach.Workplace;
using Swankers.League;

// Before the host exists: the workplace scenario gets its own local state root and drops any
// machine-wide Application Insights export. For the football scenario this changes nothing.
var isolation = WorkplaceIsolation.Apply(args);

var builder = AgentHost.CreateBuilder(args);
var configuration = builder.WebApplicationBuilder.Configuration;

// Secrets (Mcp--OwnerCredential, Mcp--CommissionerCredential) come from Key Vault when KeyVault:Uri is set.
var vaultUri = configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}

var options = configuration.GetSection(CoachOptions.SectionName).Get<CoachOptions>() ?? new CoachOptions();

// The Foundry hosted runtime injects these two; local runs set Coach:* instead.
options.ProjectEndpoint = FirstNonEmpty(options.ProjectEndpoint, configuration["FOUNDRY_PROJECT_ENDPOINT"]);
options.ModelDeployment = FirstNonEmpty(configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"], options.ModelDeployment);
if (string.IsNullOrWhiteSpace(options.ProjectEndpoint))
{
    throw new InvalidOperationException("Coach:ProjectEndpoint (or FOUNDRY_PROJECT_ENDPOINT) is required.");
}

// The scenario is chosen here and nowhere else; an unknown value stops startup. Each scenario
// has its own default prompt version; a version that does not exist for the scenario stops
// startup when its instructions are loaded, naming the versions that do.
options.Scenario = CoachScenarios.Parse(options.Scenario);
var promptVersion = CoachScenarios.PromptVersionFor(options.Scenario, options.PromptVersion);
options.PromptVersion = promptVersion;

using var startupLogging = LoggerFactory.Create(logging => logging.AddConsole());
var startupLogger = startupLogging.CreateLogger("Swankers.Coach.Startup");
var contentRoot = builder.WebApplicationBuilder.Environment.ContentRootPath;

AIAgent agent;
if (options.Scenario == CoachScenarios.Workplace)
{
    // Isolation is decided from the command line and environment before the host loads anything
    // else; a scenario chosen by some other configuration source would run without it.
    if (!isolation.Applies && !FoundryEnvironment.IsHosted)
    {
        throw new InvalidOperationException(
            "Coach:Scenario=workplace must be set on the command line or in the environment, so that its state and telemetry isolation is applied before the host starts.");
    }

    // WorkplaceMeetingPrep needs none of the football stack: no MCP credential, no league MCP
    // server, no league knowledge. Its settings are the "Workplace" section.
    var workplaceOptions = configuration.GetSection(WorkplaceOptions.SectionName).Get<WorkplaceOptions>() ?? new WorkplaceOptions();
    var workplace = await WorkplaceScenario.ComposeAsync(
        workplaceOptions, promptVersion, PromptLibrary.DefaultDirectory, contentRoot, startupLogging, CancellationToken.None);
    options.AgentName = workplace.AgentName;

    startupLogger.LogInformation(
        "Scenario workplace, evidence mode {Mode}: agent {Agent}, instructions {PromptId} (sha256 {PromptSha256}), reference time {ReferenceTime:o}, fixture {Fixture}, model {Model} at {ProjectEndpoint}, tools {Tools}.",
        workplace.Mode.ToUpperInvariant(), workplace.AgentName, workplace.PromptId, workplace.PromptSha256, workplace.ReferenceTime,
        workplace.Provider.Fixture ?? "none", options.ModelDeployment, options.ProjectEndpoint, string.Join(", ", workplace.Tools.Select(t => t.Name)));
    startupLogger.LogInformation(
        "Workplace isolation: response state under {StateRoot}; Application Insights export off (a machine-wide connection string is ignored); OTLP endpoint {OtlpEndpoint}.",
        isolation.StateRoot ?? "(hosted storage)", configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 } otlp ? otlp : "(not set)");

    // Live evidence is real Microsoft 365 content: keep it out of spans even if content capture is switched on.
    agent = CoachAgentFactory.Create(
        options, workplace.Instructions, workplace.Tools, startupLogging, WorkplaceScenario.Description,
        sensitiveTelemetry: workplace.Mode == WorkplaceModes.Live ? false : null);

    builder.Services.AddSingleton(workplace.Provider); // keeps the Work IQ session alive for the host's lifetime
}
else
{
    // The MCP credential is looked up by configuration key, so no version carries a secret value:
    // Mcp:OwnerCredential (default, hardened) or Mcp:CommissionerCredential (Friday "before").
    var mcpCredential = configuration[options.McpCredentialKey];
    if (string.IsNullOrWhiteSpace(mcpCredential))
    {
        throw new InvalidOperationException(
            $"No MCP credential at configuration key '{options.McpCredentialKey}' " +
            "(Key Vault secrets Mcp--OwnerCredential / Mcp--CommissionerCredential).");
    }

    var instructions = new PromptLibrary(PromptLibrary.DefaultDirectory).Load(promptVersion);
    startupLogger.LogInformation("Instructions {PromptVersion} loaded from {PromptsDirectory}.", promptVersion, PromptLibrary.DefaultDirectory);

    var knowledgeRoot = ResolveKnowledgeRoot(options.KnowledgeRoot, contentRoot);
    var knowledge = KnowledgeSearch.Load(knowledgeRoot);
    if (knowledge.SectionCount == 0)
    {
        throw new InvalidOperationException(
            $"No knowledge documents found under '{knowledgeRoot}' (content root '{contentRoot}', working directory '{Environment.CurrentDirectory}'). Set Coach:KnowledgeRoot.");
    }

    startupLogger.LogInformation("Loaded {Sections} knowledge sections from {KnowledgeRoot}.", knowledge.SectionCount, knowledgeRoot);

    // Connect to the league MCP server up front: a Coach with no tools is not worth starting.
    var mcpTools = new McpToolSource(options, mcpCredential, startupLogging);
    var leagueTools = await mcpTools.ConnectAsync(CancellationToken.None);
    var tools = new List<AITool>(leagueTools) { knowledge.AsTool() };

    startupLogger.LogInformation(
        "Coach {PromptVersion}: model {Model} at {ProjectEndpoint}, {McpTools} MCP tools from {McpEndpoint} (credential key {CredentialKey}), {Sections} knowledge sections from {KnowledgeRoot}.",
        promptVersion, options.ModelDeployment, options.ProjectEndpoint, leagueTools.Count, options.McpEndpoint,
        options.McpCredentialKey, knowledge.SectionCount, knowledgeRoot);

    agent = CoachAgentFactory.Create(options, instructions, tools, startupLogging);

    builder.Services.AddSingleton(mcpTools); // keeps the MCP session alive for the host's lifetime
    builder.Services.AddSingleton(knowledge);
}

builder.Services.AddSingleton(options);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

// The agent host owns the OpenTelemetry pipeline (OTLP / Application Insights from environment);
// we add our sources so agent, tool, and league spans join the same trace as the MCP server.
builder.ConfigureTracing(tracing => tracing
    .ConfigureResource(resource => resource.AddService("Swankers.Coach"))
    .AddSource(CoachDiagnostics.ActivitySourceName)
    .AddSource(OpenTelemetryAgent.DefaultSourceName)
    .AddSource(LeagueDiagnostics.ActivitySourceName));

var app = builder.Build();
app.Run();

static string FirstNonEmpty(string? first, string? second)
    => !string.IsNullOrWhiteSpace(first) ? first : second ?? "";

// From a checkout, use the repo's knowledge/ (live edits); otherwise the copy shipped next
// to the binaries, which is what a published artifact or container has.
static string ResolveKnowledgeRoot(string configured, string contentRoot)
{
    if (Path.IsPathRooted(configured))
    {
        return configured;
    }

    var fromRepo = RepoPaths.Resolve(configured, contentRoot);
    return Directory.Exists(fromRepo) && Directory.EnumerateFiles(fromRepo, "*.md").Any()
        ? fromRepo
        : Path.Combine(AppContext.BaseDirectory, "knowledge");
}
