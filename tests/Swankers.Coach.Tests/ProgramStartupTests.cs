using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Swankers.Coach.Tests;

/// <summary>
/// Program.cs itself, started as a real process: which scenario it composes and what each one
/// insists on. No model, no MCP server, no cloud: every case either stops before any connection
/// or is stopped as soon as it listens. The endpoint is a placeholder that nothing calls.
/// </summary>
public sealed class ProgramStartupTests
{
    private const string PlaceholderEndpoint = "https://example.invalid/api/projects/none";

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_no_scenario_set_the_football_coach_is_composed_and_still_needs_its_mcp_credential()
    {
        var run = await StartAsync(["--Coach:ProjectEndpoint", PlaceholderEndpoint]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("No MCP credential at configuration key 'Mcp:OwnerCredential'", run.Error);
        Assert.DoesNotContain("Scenario workplace", run.Output);
    }

    [Fact]
    public async Task Naming_the_football_scenario_is_the_same_as_the_default()
    {
        var run = await StartAsync(["--Coach:ProjectEndpoint", PlaceholderEndpoint, "--Coach:Scenario", "football"]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("No MCP credential at configuration key 'Mcp:OwnerCredential'", run.Error);
    }

    [Fact]
    public async Task An_unknown_scenario_stops_startup_and_names_the_valid_ones()
    {
        var run = await StartAsync(["--Coach:ProjectEndpoint", PlaceholderEndpoint, "--Coach:Scenario", "meetings"]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Unknown Coach:Scenario 'meetings'. Valid values: football, workplace.", run.Error);
    }

    [Fact]
    public async Task The_workplace_scenario_needs_an_explicit_mode()
    {
        var run = await StartAsync(["--Coach:ProjectEndpoint", PlaceholderEndpoint, "--Coach:Scenario", "workplace"]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Workplace:Mode must be Live or Replay", run.Error);
    }

    [Fact]
    public async Task Workplace_replay_starts_with_no_football_settings_its_own_state_root_and_no_application_insights()
    {
        var stateBase = Path.Combine(Path.GetTempPath(), "swankers-coach-tests", Path.GetRandomFileName());
        try
        {
            var run = await StartAsync(
                ["--Coach:ProjectEndpoint", PlaceholderEndpoint, "--Coach:Scenario", "workplace", "--Workplace:Mode", "Replay", "--Workplace:StateRoot", stateBase],
                environment: new()
                {
                    ["PORT"] = FreePort().ToString(),
                    // A machine-wide connection string must be ignored, and must not break startup.
                    ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                },
                stopWhenOutputContains: "Now listening");

            Assert.True(run.Listened, $"did not start:\n{run.Output}\n{run.Error}");
            Assert.Contains("Scenario workplace, evidence mode REPLAY", run.Output);
            // No Coach:PromptVersion given: the workplace scenario runs its own default, meetingprep-v1.1, not the Coach's v1.
            Assert.Contains("agent MeetingPrep-v1-1, instructions meetingprep-v1.1 (sha256 5A57E9C358E800B6A34CD23862E850D8DE5B7EA8939970FA7B16AB3E8ACAD569)", run.Output);
            Assert.Contains("tools get_engagement_evidence.", run.Output);
            Assert.Contains($"response state under {Path.Combine(stateBase, "workplace-v1.1-replay")}", run.Output);
            Assert.Contains("AppInsightsConfigured=False", run.Output);
            Assert.DoesNotContain("MCP tools from", run.Output);          // the football startup line
            Assert.DoesNotContain("knowledge sections", run.Output);
        }
        finally
        {
            if (Directory.Exists(stateBase))
            {
                Directory.Delete(stateBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_prompt_version_the_workplace_scenario_does_not_have_stops_startup_and_names_the_ones_it_has()
    {
        // v0 exists for the football Coach only; the workplace scenario must not fall back to any other version.
        var run = await StartAsync(["--Coach:ProjectEndpoint", PlaceholderEndpoint, "--Coach:Scenario", "workplace", "--Workplace:Mode", "Replay", "--Coach:PromptVersion", "v0"]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("No instructions for prompt version 'v0' (meetingprep-v0.md). Available: v1, v1.1, v2.", run.Error);
        Assert.DoesNotContain("Now listening", run.Output);
    }

    private sealed record Run(int? ExitCode, string Output, string Error, bool Listened);

    /// <summary>Starts the Coach that ships next to the test binaries and waits for it to exit or to listen.</summary>
    private static async Task<Run> StartAsync(string[] arguments, Dictionary<string, string>? environment = null, string? stopWhenOutputContains = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Swankers.Coach.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Nothing from the machine or the session may decide the outcome.
        foreach (var name in start.Environment.Keys.Where(k =>
            k.StartsWith("Coach__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Mcp__", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("Workplace__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("KeyVault__", StringComparison.OrdinalIgnoreCase)
            || k is "FOUNDRY_PROJECT_ENDPOINT" or "AZURE_AI_MODEL_DEPLOYMENT_NAME" or "APPLICATIONINSIGHTS_CONNECTION_STRING" or "OTEL_EXPORTER_OTLP_ENDPOINT" or "AGENTSERVER_STATE_ROOT").ToList())
        {
            start.Environment.Remove(name);
        }

        foreach (var (name, value) in environment ?? [])
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("could not start dotnet");
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        var listened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(e.Data);
            }

            if (stopWhenOutputContains is not null && e.Data.Contains(stopWhenOutputContains, StringComparison.Ordinal))
            {
                listened.TrySetResult();
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (error)
                {
                    error.AppendLine(e.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var exited = process.WaitForExitAsync(timeout.Token);
        try
        {
            await Task.WhenAny(exited, listened.Task);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        return new Run(listened.Task.IsCompleted ? null : process.ExitCode, Text(output), Text(error), listened.Task.IsCompleted);

        static string Text(System.Text.StringBuilder builder)
        {
            lock (builder)
            {
                return builder.ToString();
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
