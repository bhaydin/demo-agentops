using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;

namespace Swankers.AgentDeploy;

/// <summary>
/// Hosted-agent operations against the shared Foundry project: upload a code bundle as a new
/// immutable version, wait for it to go active, and point the agent endpoint at a version.
/// Verified against Azure.AI.Projects.Agents 3.0.0-beta.2 and Learn "Deploy a hosted agent
/// from source code" (updated 2026-09-21). The upload itself uses the documented REST call:
/// the SDK's only public code upload zips a folder with Windows separators in the entry names
/// (see CodeBundle), and its typed multipart path is internal in this beta.
/// </summary>
public sealed class AgentDeployer
{
    private const string Runtime = "dotnet_10";
    private const string EntryAssembly = "Swankers.Coach.dll";
    private const string ApiVersion = "v1";
    private static readonly string[] TokenScopes = ["https://ai.azure.com/.default"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // The tool runs on the maintainer's machine or a CI runner after `az login`, never with a
    // managed or workload identity; probing for one costs about two minutes off Azure (a stage
    // switch must take seconds), so those two are excluded from the default chain.
    private readonly TokenCredential _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential = true,
        ExcludeWorkloadIdentityCredential = true,
    });
    private readonly AgentAdministrationClient _admin;
    private readonly string _projectEndpoint;
    private readonly string _agentName;

    public AgentDeployer(string projectEndpoint, string agentName)
    {
        _projectEndpoint = projectEndpoint.TrimEnd('/');
        _admin = new AIProjectClient(new Uri(_projectEndpoint), _credential).AgentAdministrationClient;
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

        var (zip, sha256) = CodeBundle.Create(publishDirectory);

        // First version: POST /agents with x-ms-agent-name (returns the agent envelope).
        // Later versions: POST /agents/{name}/versions (returns the version).
        var exists = await AgentExistsAsync(ct);
        var url = exists
            ? $"{_projectEndpoint}/agents/{_agentName}/versions?api-version={ApiVersion}"
            : $"{_projectEndpoint}/agents?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var token = await _credential.GetTokenAsync(new TokenRequestContext(TokenScopes), ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-ms-code-zip-sha256", sha256);
        if (!exists)
        {
            request.Headers.Add("x-ms-agent-name", _agentName);
        }

        var metadataPart = new ByteArrayContent(ModelReaderWriter.Write(metadata, ModelReaderWriterOptions.Json).ToArray());
        metadataPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var codePart = new ByteArrayContent(zip.ToArray());
        codePart.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Content = new MultipartFormDataContent
        {
            { metadataPart, "metadata" },
            { codePart, "code", $"{_agentName}.zip" },
        };

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Upload failed: HTTP {(int)response.StatusCode} {body[..Math.Min(600, body.Length)]}");
        }

        using var document = JsonDocument.Parse(body);
        var versionJson = document.RootElement.TryGetProperty("versions", out var versions) && versions.TryGetProperty("latest", out var latest)
            ? latest.GetRawText()
            : body;
        return ModelReaderWriter.Read<ProjectsAgentVersion>(BinaryData.FromString(versionJson))
            ?? throw new InvalidOperationException("The service returned no agent version.");
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

    /// <summary>Removes a version; force so sessions still bound to it are cascaded away.</summary>
    public Task DeleteVersionAsync(string version, CancellationToken ct)
        => _admin.DeleteAgentVersionAsync(_agentName, version, force: true, ct);

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

    /// <summary>A version, the stage label its metadata carries (v1-owner, v2-owner, v0-commissioner, v0-owner), and its provisioning status.</summary>
    public sealed record StageVersion(string Version, string? Stage, string? Status)
    {
        public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    public static StageVersion ToStageVersion(ProjectsAgentVersion version)
        => new(version.Version, version.Metadata.TryGetValue("stage", out var stage) ? stage : null, version.Status.ToString());

    /// <summary>
    /// The version to route for a stage label, so demo/stage.ps1 never carries version numbers:
    /// the newest <b>active</b> version with that stage (v4 and v7 are both v1-owner; the newer
    /// is the one CI promoted). A failed or still-creating version is never chosen (Codex
    /// Phase 7); unknown labels list what exists.
    /// </summary>
    public static string ResolveLabel(IEnumerable<StageVersion> versions, string label)
    {
        var all = versions.ToList();
        var labelled = all.Where(v => string.Equals(v.Stage, label, StringComparison.OrdinalIgnoreCase)).ToList();
        if (labelled.Count == 0)
        {
            var stages = all.Where(v => v.Stage is not null).Select(v => $"{v.Stage} (v{v.Version})").Distinct().Order(StringComparer.Ordinal);
            throw new InvalidOperationException($"No version has stage '{label}'. Available: {string.Join(", ", stages)}.");
        }

        var active = labelled
            .Where(v => v.IsActive)
            .OrderByDescending(v => int.TryParse(v.Version, out var n) ? n : -1)
            .ThenByDescending(v => v.Version, StringComparer.Ordinal)
            .ToList();
        if (active.Count == 0)
        {
            var states = string.Join(", ", labelled.Select(v => $"v{v.Version} {v.Status ?? "unknown"}"));
            throw new InvalidOperationException($"No active version has stage '{label}' ({states}). Route with --version once one is active.");
        }

        return active[0].Version;
    }

    private async Task<bool> AgentExistsAsync(CancellationToken ct)
    {
        try
        {
            await _admin.GetAgentAsync(_agentName, ct);
            return true;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
