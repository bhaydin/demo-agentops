using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;

namespace Swankers.AgentDeploy;

/// <summary>
/// Hosted-agent operations against the shared Foundry project: upload a code bundle as a new
/// immutable version, wait for it to go active, and point the agent endpoint at a version.
/// Verified against Azure.AI.Projects.Agents 3.0.0-beta.2 and Learn "Deploy a hosted agent
/// from source code" (updated 2026-09-21).
/// </summary>
public sealed class AgentDeployer
{
    private const string Runtime = "dotnet_10";
    private const string EntryAssembly = "Swankers.Coach.dll";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly AgentAdministrationClient _admin;
    private readonly string _agentName;

    public AgentDeployer(string projectEndpoint, string agentName)
    {
        _admin = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential()).AgentAdministrationClient;
        _agentName = agentName;
    }

    public async Task<ProjectsAgentVersion> CreateVersionAsync(CoachVersionSpec spec, string publishDirectory, CancellationToken ct)
    {
        var definition = new HostedAgentDefinition(cpu: spec.Cpu, memory: spec.Memory)
        {
            Versions = { new ProtocolVersionRecord(ProjectsAgentProtocol.Responses, "2.0.0") },
            // Bundled: the zip is the publish output and runs as-is; nothing is built server-side.
            CodeConfiguration = new CodeConfiguration(Runtime, ["dotnet", EntryAssembly], CodeDependencyResolution.Bundled),
        };
        foreach (var (name, value) in spec.EnvironmentVariables)
        {
            definition.EnvironmentVariables[name] = value;
        }

        var metadata = new AgentVersionFromCodeMetadata(definition) { Description = spec.Description };
        foreach (var (name, value) in spec.Metadata)
        {
            metadata.Metadata[name] = value;
        }

        // The SDK zips the directory (flat at the root) and sends the SHA-256 for integrity.
        return await _admin.CreateAgentVersionFromCodeAsync(_agentName, publishDirectory, metadata, ct);
    }

    public async Task<ProjectsAgentVersion> WaitForActiveAsync(string version, TimeSpan timeout, Action<string> log, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            ProjectsAgentVersion current = await _admin.GetAgentVersionAsync(_agentName, version, ct);
            log($"{_agentName} v{current.Version}: {current.Status}");
            if (current.Status == AgentVersionStatus.Active)
            {
                return current;
            }

            if (current.Status == AgentVersionStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"{_agentName} v{current.Version} failed to provision. Inspect the version in the Foundry portal for the error detail.");
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"{_agentName} v{current.Version} is still {current.Status} after {timeout}.");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>Sends 100% of endpoint traffic to one version; this is the rollback lever.</summary>
    public Task RouteAsync(string version, CancellationToken ct)
    {
        var options = new PatchAgentOptions
        {
            AgentEndpoint = new AgentEndpointConfiguration
            {
                VersionSelector = new VersionSelector([new FixedRatioVersionSelectionRule(version, 100)]),
                ProtocolConfiguration = new ProtocolConfiguration { Responses = new ResponsesProtocolConfiguration() },
            },
        };
        return _admin.PatchAgentAsync(_agentName, options, ct);
    }

    public async Task<ProjectsAgentRecord> GetAgentAsync(CancellationToken ct)
        => await _admin.GetAgentAsync(_agentName, ct);

    public async Task<IReadOnlyList<ProjectsAgentVersion>> ListVersionsAsync(CancellationToken ct)
    {
        var versions = new List<ProjectsAgentVersion>();
        await foreach (var version in _admin.GetAgentVersionsAsync(_agentName, cancellationToken: ct))
        {
            versions.Add(version);
        }

        return versions;
    }

    public static string? RoutedVersion(ProjectsAgentRecord agent)
        => agent.AgentEndpoint?.VersionSelector?.VersionSelectionRules
            .OfType<FixedRatioVersionSelectionRule>()
            .OrderByDescending(rule => rule.TrafficPercentage)
            .FirstOrDefault()?.AgentVersion;
}
