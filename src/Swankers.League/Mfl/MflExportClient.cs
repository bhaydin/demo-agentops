using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swankers.League.Models;
using Swankers.League.Snapshots;

namespace Swankers.League.Mfl;

/// <summary>
/// Read-only MFL gateway: export requests only (enforced by <see cref="MflRequestBuilder"/> and
/// <see cref="ExportOnlyHandler"/>), registered User-Agent on every call, requests spaced per
/// MFL's guidance, responses cached, and a snapshot fallback when MFL is unavailable. Never
/// implements ILeagueWriter. Logs request types and statuses only — never URLs, never bodies
/// (the league export contains owner PII).
/// </summary>
public sealed partial class MflExportClient : ILeagueReader
{
    private readonly HttpClient _http;
    private readonly MflOptions _options;
    private readonly IMemoryCache _cache;
    private readonly FranchiseNameMap _names;
    private readonly ILogger<MflExportClient> _logger;
    private readonly ILeagueReader? _fallback;
    private readonly MflRateLimiter _rateLimiter;
    private readonly MflRequestBuilder _requests;

    public MflExportClient(
        HttpClient http,
        IOptions<MflOptions> options,
        IMemoryCache cache,
        FranchiseNameMap names,
        ILogger<MflExportClient> logger,
        MflRateLimiter? rateLimiter = null,
        ISnapshotLeagueReader? snapshotFallback = null)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _names = names;
        _logger = logger;
        _rateLimiter = rateLimiter ?? new MflRateLimiter();
        _fallback = snapshotFallback;
        _requests = new MflRequestBuilder(_options);

        // Registered client User-Agent, required by MFL on every request.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken)
        => GetAsync(
            "mfl:franchises", _options.CacheDuration, "league", _requests.League,
            ParseFranchises,
            static (reader, ct) => reader.GetFranchisesAsync(ct),
            cancellationToken);

    public Task<IReadOnlyList<Player>> GetPlayersAsync(CancellationToken cancellationToken)
        => GetAsync(
            "mfl:players", _options.PlayerCacheDuration, "players", _requests.Players,
            ParsePlayers,
            static (reader, ct) => reader.GetPlayersAsync(ct),
            cancellationToken);

    public Task<IReadOnlyList<Roster>> GetRostersAsync(CancellationToken cancellationToken)
        => GetAsync(
            "mfl:rosters", _options.CacheDuration, "rosters", _requests.Rosters,
            ParseRosters,
            static (reader, ct) => reader.GetRostersAsync(ct),
            cancellationToken);

    public async Task<Roster> GetRosterAsync(string franchiseId, CancellationToken cancellationToken)
    {
        var rosters = await GetRostersAsync(cancellationToken);
        return rosters.FirstOrDefault(r => r.FranchiseId == franchiseId)
            ?? new Roster(franchiseId, []);
    }

    public Task<IReadOnlyList<Injury>> GetInjuriesAsync(int? week, CancellationToken cancellationToken)
        => GetAsync(
            $"mfl:injuries:{week?.ToString() ?? "current"}", _options.CacheDuration, "injuries",
            () => _requests.Injuries(week),
            ParseInjuries,
            (reader, ct) => reader.GetInjuriesAsync(week, ct),
            cancellationToken);

    public Task<IReadOnlyList<Matchup>> GetMatchupsAsync(int week, CancellationToken cancellationToken)
        => GetAsync(
            $"mfl:schedule:{week}", _options.CacheDuration, "schedule",
            () => _requests.Schedule(week),
            root => ParseMatchups(root, week),
            (reader, ct) => reader.GetMatchupsAsync(week, ct),
            cancellationToken);

    public Task<IReadOnlyList<Projection>> GetProjectionsAsync(int week, CancellationToken cancellationToken)
        => GetAsync(
            $"mfl:projections:{week}", _options.CacheDuration, "projectedScores",
            () => _requests.ProjectedScores(week),
            root => ParseProjections(root, week),
            (reader, ct) => reader.GetProjectionsAsync(week, ct),
            cancellationToken);

    public Task<IReadOnlyList<WeeklyResult>> GetWeeklyResultsAsync(int week, CancellationToken cancellationToken)
        => GetAsync(
            $"mfl:weeklyResults:{week}", _options.CacheDuration, "weeklyResults",
            () => _requests.WeeklyResults(week),
            root => ParseWeeklyResults(root, week),
            (reader, ct) => reader.GetWeeklyResultsAsync(week, ct),
            cancellationToken);

    public Task<IReadOnlyList<Standing>> GetStandingsAsync(CancellationToken cancellationToken)
        => GetAsync(
            "mfl:standings", _options.CacheDuration, "leagueStandings", _requests.LeagueStandings,
            ParseStandings,
            static (reader, ct) => reader.GetStandingsAsync(ct),
            cancellationToken);

    public Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int count, CancellationToken cancellationToken)
        => GetAsync(
            $"mfl:transactions:{count}", _options.CacheDuration, "transactions",
            () => _requests.Transactions(count),
            ParseTransactions,
            (reader, ct) => reader.GetTransactionsAsync(count, ct),
            cancellationToken);

    public async Task<IReadOnlyList<Trade>> GetPendingTradesAsync(
        string franchiseId,
        CancellationToken cancellationToken)
    {
        var trades = await GetAsync(
            "mfl:pendingTrades", _options.CacheDuration, "pendingTrades", _requests.PendingTrades,
            ParsePendingTrades,
            (reader, ct) => reader.GetPendingTradesAsync(franchiseId, ct),
            cancellationToken);

        return [.. trades.Where(t => t.FromFranchiseId == franchiseId || t.ToFranchiseId == franchiseId)];
    }

    private async Task<T> GetAsync<T>(
        string cacheKey,
        TimeSpan cacheDuration,
        string type,
        Func<Uri> requestUri,
        Func<JsonElement, T> parse,
        Func<ILeagueReader, CancellationToken, Task<T>> fallback,
        CancellationToken cancellationToken)
        where T : class
    {
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var document = await FetchAsync(type, requestUri(), cancellationToken);
            var result = parse(document.RootElement);
            _cache.Set(cacheKey, result, cacheDuration);
            return result;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            if (_fallback is null)
            {
                throw;
            }

            // Per MFL guidance a failed request is not retried; serve the latest snapshot.
            _logger.LogWarning(ex, "MFL export {Type} unavailable; serving snapshot fallback.", type);
            return await fallback(_fallback, cancellationToken);
        }
    }

    private async Task<JsonDocument> FetchAsync(string type, Uri uri, CancellationToken cancellationToken)
    {
        using var activity = LeagueDiagnostics.ActivitySource.StartActivity("mfl.export");
        activity?.SetTag("mfl.type", type);

        return await _rateLimiter.RunAsync(_options.MinRequestSpacing, async () =>
        {
            using var response = await _http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            _logger.LogDebug("MFL export {Type} succeeded.", type);
            return document;
        }, cancellationToken);
    }
}
