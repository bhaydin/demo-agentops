// Swankers.Web: the stage. Chat with the deployed Coach on the left, the league ticker on the
// right, an approval dialog when the confirmation gate holds an action, and a header that says
// which agent version and credential scope are running. The app talks to Foundry (chat, routed
// version) and to Swankers.Mcp's demo REST API (state, approvals); it never calls MCP tools.
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Swankers.Web;
using Swankers.Web.Coach;
using Swankers.Web.Components;
using Swankers.Web.League;

var builder = WebApplication.CreateBuilder(args);

// Mcp--DemoAdminKey comes from Key Vault when KeyVault:Uri is set (managed identity in Azure).
var vaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}

builder.Services
    .AddOptions<McpApiOptions>()
    .Bind(builder.Configuration.GetSection(McpApiOptions.SectionName))
    .Validate(o => Uri.IsWellFormedUriString(o.BaseUrl, UriKind.Absolute), "Mcp:BaseUrl must be an absolute URL.")
    .Validate(o => o.DemoAdminKey.Length > 0, "Mcp:DemoAdminKey is required (Key Vault: Mcp--DemoAdminKey).")
    .ValidateOnStart();

builder.Services
    .AddOptions<CoachEndpointOptions>()
    .Bind(builder.Configuration.GetSection(CoachEndpointOptions.SectionName))
    .Validate(o => Uri.IsWellFormedUriString(o.ProjectEndpoint, UriKind.Absolute), "Coach:ProjectEndpoint must be the Foundry project endpoint.")
    .Validate(o => o.AgentName.Length > 0, "Coach:AgentName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

// The demo REST API: base address and admin key on every request. Tests swap the handler.
builder.Services.AddHttpClient<LeagueStateClient>((sp, http) =>
{
    var mcp = sp.GetRequiredService<IOptions<McpApiOptions>>().Value;
    http.BaseAddress = new Uri(mcp.BaseUrl);
    http.DefaultRequestHeaders.Add("X-Demo-Admin-Key", mcp.DemoAdminKey);
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<LeagueStateFeed>();

// Foundry: the deployed Coach (chat) and its routed version (header). Managed identity in Azure,
// az login locally; the web identity holds Foundry User on the account.
builder.Services.AddSingleton(sp =>
{
    var coach = sp.GetRequiredService<IOptions<CoachEndpointOptions>>().Value;
    return new AIProjectClient(new Uri(coach.ProjectEndpoint), new DefaultAzureCredential());
});
builder.Services.AddSingleton<ICoachChat, CoachChat>();
builder.Services.AddSingleton<IAgentVersionInfo, AgentVersionInfo>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Swankers.Web"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(WebDiagnostics.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }

        // Application Insights, the same resource the Coach and MCP trace into: one operation
        // from the chat click through the agent, the tool call, and the league write.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            tracing.AddAzureMonitorTraceExporter(o =>
            {
                o.SamplingRatio = 1.0f;
                o.TracesPerSecond = null;
            });
        }
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Fingerprinted static assets need the build/publish manifest; under WebApplicationFactory
// (tests) there is none, so plain static files serve wwwroot instead.
var assetsManifest = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.staticwebassets.endpoints.json");
if (File.Exists(assetsManifest))
{
    app.MapStaticAssets();
}
else
{
    app.UseStaticFiles();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
