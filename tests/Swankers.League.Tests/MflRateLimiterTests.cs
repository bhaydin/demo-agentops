using Microsoft.Extensions.DependencyInjection;
using Swankers.League.Mfl;
using Swankers.League.Tests.Support;

namespace Swankers.League.Tests;

/// <summary>Codex Phase 1 review #7: spacing must hold across client instances, not per instance.</summary>
public class MflRateLimiterTests
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MinimumObservedGap = TimeSpan.FromMilliseconds(120);

    [Fact]
    public async Task Separately_resolved_clients_share_one_request_spacing()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MflOptions>(o =>
        {
            var defaults = MflTest.Options(Spacing);
            o.ApiKey = defaults.ApiKey;
            o.LeagueId = defaults.LeagueId;
            o.Host = defaults.Host;
            o.UserAgent = defaults.UserAgent;
            o.MinRequestSpacing = defaults.MinRequestSpacing;
        });
        services.AddMflExportClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var ct = TestContext.Current.CancellationToken;

        var first = provider.GetRequiredService<MflExportClient>();
        var second = provider.GetRequiredService<MflExportClient>();
        Assert.NotSame(first, second); // typed clients are created per resolution

        await first.GetPlayersAsync(ct);
        await second.GetInjuriesAsync(1, ct);

        Assert.Equal(2, handler.Calls.Count);
        var gap = handler.Calls[1].TimestampUtc - handler.Calls[0].TimestampUtc;
        Assert.True(gap >= MinimumObservedGap, $"requests only {gap.TotalMilliseconds}ms apart across two clients");
    }

    [Fact]
    public async Task Concurrent_requests_through_one_limiter_are_serialized_and_spaced()
    {
        var limiter = new MflRateLimiter();
        var handler = new RecordingHandler();
        var (a, _, _) = MflTest.Create(options: MflTest.Options(Spacing), rateLimiter: limiter, handler: handler);
        var (b, _, _) = MflTest.Create(options: MflTest.Options(Spacing), rateLimiter: limiter, handler: handler);
        var ct = TestContext.Current.CancellationToken;

        await Task.WhenAll(a.GetPlayersAsync(ct), b.GetInjuriesAsync(1, ct), a.GetStandingsAsync(ct));

        Assert.Equal(3, handler.Calls.Count);
        var ordered = handler.Calls.OrderBy(c => c.TimestampUtc).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].TimestampUtc - ordered[i - 1].TimestampUtc;
            Assert.True(gap >= MinimumObservedGap, $"call {i} only {gap.TotalMilliseconds}ms after the previous one");
        }
    }
}
