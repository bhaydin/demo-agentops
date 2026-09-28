namespace Swankers.League.Mfl;

/// <summary>
/// Process-wide request spacing for MFL. Registered as a singleton and shared by every
/// <see cref="MflExportClient"/> (DI creates typed clients per resolution), so MFL's
/// "one second between requests" guidance holds across instances, not just within one.
/// </summary>
public sealed class MflRateLimiter(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequestUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Runs <paramref name="request"/> no sooner than <paramref name="spacing"/> after the
    /// previous request finished. Callers are serialized, so concurrent requests queue.
    /// </summary>
    public async Task<T> RunAsync<T>(TimeSpan spacing, Func<Task<T>> request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _nextRequestUtc - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            try
            {
                return await request();
            }
            finally
            {
                _nextRequestUtc = _time.GetUtcNow() + spacing;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
