using Microsoft.Extensions.Options;
using Swankers.League.Mfl;
using Swankers.League.Models;
using Swankers.League.Sim;

namespace Swankers.Mcp.Tools;

/// <summary>Where get_player_news reads injuries from.</summary>
public interface IInjurySource
{
    string SourceName { get; }

    Task<IReadOnlyList<Injury>> GetInjuriesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Live MFL injury report when MFL credentials are configured (with the client's snapshot
/// fallback); otherwise the injuries captured in the snapshot SimLeague was seeded from.
/// </summary>
public sealed class InjurySource(IOptions<MflOptions> mfl, MflExportClient live, SimLeague sim) : IInjurySource
{
    private bool LiveConfigured => mfl.Value.ApiKey.Length > 0 && mfl.Value.Host.Length > 0;

    public string SourceName => LiveConfigured ? "mfl-live" : "snapshot";

    public Task<IReadOnlyList<Injury>> GetInjuriesAsync(CancellationToken cancellationToken)
        => LiveConfigured
            ? live.GetInjuriesAsync(null, cancellationToken)
            : sim.GetInjuriesAsync(null, cancellationToken);
}
