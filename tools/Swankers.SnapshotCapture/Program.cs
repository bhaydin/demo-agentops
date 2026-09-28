// Snapshot capture: pulls MFL exports into data/snapshot/<date>/, anonymized by construction.
// THE MAINTAINER RUNS THIS (CLAUDE.md: agents never run it against the live MFL API).
// Reads are export-only via MflExportClient; this tool has no write path to any platform.
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swankers.League;
using Swankers.League.Mfl;
using Swankers.League.Snapshots;
using Swankers.SnapshotCapture;

var builder = Host.CreateApplicationBuilder(args);

// Secrets come from Key Vault (Mfl--ApiKey etc. map to Mfl:*); KeyVault:Uri can live in
// appsettings/env/args. DefaultAzureCredential covers az login locally.
var vaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}

builder.Services
    .AddOptions<MflOptions>()
    .Bind(builder.Configuration.GetSection(MflOptions.SectionName))
    .Validate(
        o => o is { ApiKey.Length: > 0, LeagueId.Length: > 0, Host.Length: > 0, UserAgent.Length: > 0 },
        "Mfl options incomplete: Mfl:ApiKey, Mfl:LeagueId, Mfl:Host, and Mfl:UserAgent are required.")
    .ValidateOnStart();

var options = new CaptureOptions();
builder.Configuration.GetSection(CaptureOptions.SectionName).Bind(options);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(
    await FranchiseNameMap.LoadAsync(options.FranchiseNamesPath, CancellationToken.None));
builder.Services.AddSingleton(new SnapshotStore(options.OutputRoot));
builder.Services.AddMflExportClient();
// The capture pipeline reads through the live export client (never SimLeague).
builder.Services.AddTransient<ILeagueReader>(sp => sp.GetRequiredService<MflExportClient>());
builder.Services.AddSingleton<CaptureService>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

try
{
    var snapshotId = await host.Services.GetRequiredService<CaptureService>()
        .CaptureAsync(cancellation.Token);
    Console.WriteLine($"Snapshot '{snapshotId}' written under {options.OutputRoot}.");
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // Message only: never echo configuration, URLs, or response content.
    Console.Error.WriteLine($"Capture failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
