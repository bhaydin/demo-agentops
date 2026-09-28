// Swankers.Mcp: C# MCP server (streamable HTTP) over the league gateway, plus the demo REST API.
// Scope comes from the credential, irreversible tools wait for human approval, approval is
// never a tool. Vulnerable paths are marked DEMO and must be selected explicitly in config.
using Azure.Identity;
using ModelContextProtocol.Protocol;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Swankers.League;
using Swankers.League.Mfl;
using Swankers.League.Sim;
using Swankers.League.Snapshots;
using Swankers.Mcp;
using Swankers.Mcp.Api;
using Swankers.Mcp.Gate;
using Swankers.Mcp.Scenarios;
using Swankers.Mcp.Security;
using Swankers.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Secrets (Mcp--*, Mfl--*) come from Key Vault when KeyVault:Uri is set.
var vaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}

builder.Services
    .AddOptions<McpOptions>()
    .Bind(builder.Configuration.GetSection(McpOptions.SectionName))
    .Validate(
        o => o.OwnerCredential.Length > 0 && o.DemoAdminKey.Length > 0,
        "Mcp:OwnerCredential and Mcp:DemoAdminKey are required (Key Vault: Mcp--OwnerCredential, Mcp--DemoAdminKey).")
    .ValidateOnStart();

// Optional: live injury reports when MFL credentials are configured.
builder.Services.AddOptions<MflOptions>().Bind(builder.Configuration.GetSection(MflOptions.SectionName));

var paths = builder.Configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
builder.Services.AddSingleton(new SnapshotStore(paths.SnapshotRoot));
builder.Services.AddSingleton<ISnapshotLeagueReader>(sp => new SnapshotLeagueReader(sp.GetRequiredService<SnapshotStore>()));
builder.Services.AddSingleton(sp => new SimLeague(
    sp.GetRequiredService<SnapshotStore>(), paths.StateDirectory, logger: sp.GetRequiredService<ILogger<SimLeague>>()));
builder.Services.AddMflExportClient();
builder.Services.AddTransient<IInjurySource, InjurySource>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CredentialResolver>();
builder.Services.AddSingleton<CallerScopeAccessor>();
builder.Services.AddSingleton<ConfirmationGate>();
builder.Services.AddSingleton<ToolRunner>();
builder.Services.AddSingleton<LeagueViews>();
builder.Services.AddSingleton<ScenarioSeeder>();

builder.Services
    .AddMcpServer(o => o.ServerInfo = new Implementation { Name = "swankers-coach-league", Version = "0.2.0" })
    .WithHttpTransport()
    .WithTools<ReadTools>()
    .WithTools<WriteTools>()
    .WithTools<IrreversibleTools>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Swankers.Mcp"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(McpDiagnostics.ActivitySourceName)
            .AddSource(LeagueDiagnostics.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }
    });

var app = builder.Build();

// Every MCP request needs a known credential; the resolved scope rides on the request.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<CredentialMiddleware>());

app.MapMcp("/mcp");
app.MapDemoApi();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
