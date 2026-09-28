using Microsoft.Extensions.Options;
using Swankers.League.Mfl;
using Swankers.League.Models;
using Swankers.League.Sim;

namespace Swankers.Mcp.Tools;

/// <summary>Injuries plus where they actually came from and how fresh they are.</summary>
public sealed record InjuryReport(IReadOnlyList<Injury> Injuries, string Source, DateTimeOffset? AsOf);

/// <summary>Where get_player_news reads injuries from.</summary>
public interface IInjurySource
{
    Task<InjuryReport> GetInjuriesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Live MFL injury report when MFL credentials are configured, labeled by what the client
/// actually served (mfl-live, mfl-cache, or the snapshot it fell back to); otherwise the
/// injuries in SimLeague's state, labeled with the snapshot that state was seeded from (not
/// whatever snapshot happens to be latest on disk).
/// </summary>
public sealed class InjurySource(IOptions<MflOptions> mfl, MflExportClient live, SimLeague sim) : IInjurySource
{
    private bool LiveConfigured => mfl.Value.ApiKey.Length > 0 && mfl.Value.Host.Length > 0;

    public async Task<InjuryReport> GetInjuriesAsync(CancellationToken cancellationToken)
    {
        if (LiveConfigured)
        {
            var sourced = await live.GetInjuriesWithSourceAsync(null, cancellationToken);
            return new InjuryReport(sourced.Value, Describe(sourced), sourced.AsOf);
        }

        var seed = await sim.GetSeedAsync(cancellationToken);
        return new InjuryReport(
            await sim.GetInjuriesAsync(null, cancellationToken),
            $"snapshot {seed.SnapshotId}",
            seed.CapturedAtUtc);
    }

    private static string Describe(Sourced<IReadOnlyList<Injury>> sourced) => sourced.Source switch
    {
        DataSource.Live => "mfl-live",
        DataSource.Cache => "mfl-cache",
        _ => sourced.SnapshotId is null ? "snapshot" : $"snapshot {sourced.SnapshotId}",
    };
}
