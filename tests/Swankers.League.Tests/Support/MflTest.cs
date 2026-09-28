using Microsoft.Extensions.Caching.Memory;
using Swankers.League;
using Swankers.League.Mfl;
using Swankers.League.Snapshots;

namespace Swankers.League.Tests.Support;

internal static class MflTest
{
    public const string ApiKey = "TEST-KEY-do-not-log";

    public static MflOptions Options(TimeSpan? spacing = null) => new()
    {
        ApiKey = ApiKey,
        LeagueId = "12345",
        Host = "www42.myfantasyleague.com",
        UserAgent = "SwankersTest/1.0",
        Year = 2026,
        MinRequestSpacing = spacing ?? TimeSpan.Zero,
    };

    /// <summary>Builds a client with the production handler chain: ExportOnlyHandler → stub.</summary>
    public static (MflExportClient Client, RecordingHandler Handler, CapturingLogger<MflExportClient> Logger) Create(
        Func<Uri, HttpResponseMessage>? responder = null,
        ISnapshotLeagueReader? fallback = null,
        MflOptions? options = null,
        FranchiseNameMap? names = null)
    {
        var handler = new RecordingHandler(responder);
        var http = new HttpClient(new ExportOnlyHandler { InnerHandler = handler });
        var logger = new CapturingLogger<MflExportClient>();

        var client = new MflExportClient(
            http,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            new MemoryCache(new MemoryCacheOptions()),
            names ?? FranchiseNameMap.Empty,
            logger,
            time: null,
            snapshotFallback: fallback);

        return (client, handler, logger);
    }
}
