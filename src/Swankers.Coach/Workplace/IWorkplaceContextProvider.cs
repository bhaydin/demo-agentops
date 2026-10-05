namespace Swankers.Coach.Workplace;

/// <summary>
/// Where the workplace scenario's evidence comes from. Two implementations sit behind it:
/// <see cref="WorkIqContextProvider"/> (Live, real Microsoft 365 content through Work IQ) and
/// <see cref="FixtureContextProvider"/> (Replay, a versioned synthetic fixture). The agent sees
/// the same single tool either way, so prompt versions can be compared over fixed evidence.
/// </summary>
public interface IWorkplaceContextProvider : IAsyncDisposable
{
    /// <summary><see cref="WorkplaceModes.Live"/> or <see cref="WorkplaceModes.Replay"/>.</summary>
    string Mode { get; }

    /// <summary>The fixture a Replay provider serves; null when Live.</summary>
    string? Fixture { get; }

    Task<IReadOnlyList<EvidenceItem>> GetEvidenceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Live or Replay, always chosen explicitly. A failed Live retrieval is reported as a failure;
/// no code path substitutes Replay for it.
/// </summary>
public static class WorkplaceModes
{
    public const string Live = "live";
    public const string Replay = "replay";

    public static string Parse(string? configured) => configured?.Trim().ToLowerInvariant() switch
    {
        Live => Live,
        Replay => Replay,
        _ => throw new InvalidOperationException(
            $"Workplace:Mode must be Live or Replay (got '{configured}'). There is no default: Replay never stands in for Live."),
    };
}

/// <summary>Span attribute names for the workplace scenario. Values are metadata only, never content.</summary>
public static class WorkplaceTags
{
    public const string Scenario = "swankers.scenario";
    public const string Mode = "swankers.workplace.mode";
    public const string Fixture = "swankers.workplace.fixture";
    public const string Items = "swankers.workplace.items";
    public const string ItemsDropped = "swankers.workplace.items_dropped";
    public const string ToolName = "mcp.tool.name";
}
