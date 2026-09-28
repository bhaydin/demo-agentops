// Swankers.Coach: Microsoft Agent Framework agent hosted through the Foundry Responses
// protocol (AgentHost + AddFoundryResponses/MapFoundryResponses). Tools come from Swankers.Mcp
// over MCP with the configured credential, plus local league-knowledge search. The stage
// configurations differ only in Coach:PromptVersion and Coach:McpCredentialKey.
using Azure.AI.AgentServer.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using OpenTelemetry.Trace;
using Swankers.Coach;
using Swankers.Coach.Knowledge;
using Swankers.Coach.Mcp;
using Swankers.Coach.Prompts;
using Swankers.League;

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

// The MCP credential is looked up by configuration key, so no version carries a secret value:
// Mcp:OwnerCredential (default, hardened) or Mcp:CommissionerCredential (Friday "before").
var mcpCredential = configuration[options.McpCredentialKey];
if (string.IsNullOrWhiteSpace(mcpCredential))
{
    throw new InvalidOperationException(
        $"No MCP credential at configuration key '{options.McpCredentialKey}' " +
        "(Key Vault secrets Mcp--OwnerCredential / Mcp--CommissionerCredential).");
}

using var startupLogging = LoggerFactory.Create(logging => logging.AddConsole());
var startupLogger = startupLogging.CreateLogger("Swankers.Coach.Startup");
var contentRoot = builder.WebApplicationBuilder.Environment.ContentRootPath;

var instructions = new PromptLibrary(PromptLibrary.DefaultDirectory).Load(options.PromptVersion);
startupLogger.LogInformation("Instructions {PromptVersion} loaded from {PromptsDirectory}.", options.PromptVersion, PromptLibrary.DefaultDirectory);

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
    options.PromptVersion, options.ModelDeployment, options.ProjectEndpoint, leagueTools.Count, options.McpEndpoint,
    options.McpCredentialKey, knowledge.SectionCount, knowledgeRoot);

var agent = CoachAgentFactory.Create(options, instructions, tools, startupLogging);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(mcpTools); // keeps the MCP session alive for the host's lifetime
builder.Services.AddSingleton(knowledge);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

// The agent host owns the OpenTelemetry pipeline (OTLP / Application Insights from environment);
// we add our sources so agent, tool, and league spans join the same trace as the MCP server.
builder.ConfigureTracing(tracing => tracing
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
