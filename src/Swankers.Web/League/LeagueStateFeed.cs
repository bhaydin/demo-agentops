using System.Text.Json;

namespace Swankers.Web.League;

/// <summary>
/// One circuit's view of the league: polls GET /api/state every two seconds (the architecture's
/// ticker cadence) and tells the header, ticker, and approval dialog when it changed. Scoped, so
/// each browser tab has its own poll and the loop ends with the circuit.
/// </summary>
public sealed class LeagueStateFeed(LeagueStateClient client, ILogger<LeagueStateFeed> logger) : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public LeagueState Current { get; private set; } = LeagueState.Empty;

    /// <summary>Set while the MCP server cannot be reached; the last good state stays visible.</summary>
    public string? Error { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Raised after every poll, on a background thread: components marshal with InvokeAsync.</summary>
    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            Current = await client.GetStateAsync(ct);
            Error = null;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Error = ex.Message;
            logger.LogWarning(ex, "League state poll failed; keeping the last state.");
        }

        Changed?.Invoke();
    }

    /// <summary>Starts the poll once; any component may call it after its first interactive render.</summary>
    public void EnsurePolling()
    {
        _loop ??= PollAsync(_stop.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The circuit ended.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _stop.Dispose();
    }
}
