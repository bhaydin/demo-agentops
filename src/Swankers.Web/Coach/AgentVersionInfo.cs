using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Options;

namespace Swankers.Web.Coach;

/// <summary>
/// Reads which Coach version the agent endpoint routes to and how that version is configured
/// (the metadata AgentDeploy stamps: prompt, credential). Cached briefly so the header follows
/// a route change within seconds without a request per render. Failures degrade the header,
/// never the page.
/// </summary>
public sealed class AgentVersionInfo(
    AIProjectClient project,
    IOptions<CoachEndpointOptions> options,
    TimeProvider time,
    ILogger<AgentVersionInfo> logger) : IAgentVersionInfo
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private AgentVersionSummary? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<AgentVersionSummary> GetAsync(CancellationToken ct)
    {
        if (Fresh() is { } fresh)
        {
            return fresh;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            if (Fresh() is { } cached)
            {
                return cached;
            }

            _cached = await ReadAsync(ct);
            _cachedAt = time.GetUtcNow();
            return _cached;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private AgentVersionSummary? Fresh()
        => _cached is not null && time.GetUtcNow() - _cachedAt < CacheFor ? _cached : null;

    private async Task<AgentVersionSummary> ReadAsync(CancellationToken ct)
    {
        var name = options.Value.AgentName;
        try
        {
            var admin = project.AgentAdministrationClient;
            ProjectsAgentRecord agent = await admin.GetAgentAsync(name, ct);
            var routed = RoutedVersion(agent);
            if (routed is null)
            {
                return AgentVersionSummary.Unknown(name, "no version is routed");
            }

            ProjectsAgentVersion version = await admin.GetAgentVersionAsync(name, routed, ct);
            return new AgentVersionSummary(name, routed, version.Description, Metadata(version, "prompt"), Metadata(version, "credential"), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the routed version of agent {AgentName}.", name);
            return AgentVersionSummary.Unknown(name, ex.Message);
        }
    }

    /// <summary>The version the endpoint sends traffic to (the highest fixed-ratio rule).</summary>
    public static string? RoutedVersion(ProjectsAgentRecord agent)
        => agent.AgentEndpoint?.VersionSelector?.VersionSelectionRules
            .OfType<FixedRatioVersionSelectionRule>()
            .OrderByDescending(rule => rule.TrafficPercentage)
            .FirstOrDefault()?.AgentVersion;

    private static string? Metadata(ProjectsAgentVersion version, string key)
        => version.Metadata is { } metadata && metadata.TryGetValue(key, out var value) ? value : null;
}
