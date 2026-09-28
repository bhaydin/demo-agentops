using System.Collections.Concurrent;
using System.Security.Cryptography;
using ModelContextProtocol;

namespace Swankers.Mcp.Gate;

/// <summary>A gated irreversible action waiting for a human decision.</summary>
public sealed record PendingConfirmation(
    string Id,
    string Tool,
    string Summary,
    string CallerScope,
    string EffectiveFranchiseId,
    IReadOnlyDictionary<string, object?> Arguments,
    DateTimeOffset CreatedAtUtc);

/// <summary><paramref name="Status"/> is executed | denied | failed | canceled.</summary>
public sealed record ResolvedConfirmation(
    PendingConfirmation Confirmation,
    bool Approved,
    string Status,
    DateTimeOffset ResolvedAtUtc,
    string? Error);

public sealed record ConfirmationOutcome(bool Approved, string Status, object? Result, string? Error);

/// <summary>What the agent gets back instead of a result when the gate holds an action.</summary>
public sealed record PendingConfirmationResult(string Status, string ConfirmationId, string Summary);

/// <summary>
/// Holds irreversible actions until a human approves or denies them through the REST endpoint.
/// Approval is deliberately not an MCP tool: nothing here is reachable from the agent except
/// <see cref="Create"/>, so the agent cannot approve its own actions. Every decision ends in a
/// terminal record (executed, denied, failed, canceled); a failed action is not retried, the
/// agent asks again and a new confirmation is created.
/// </summary>
/// <remarks>
/// A league reset advances a generation. A tool call reads <see cref="Generation"/> before it
/// prepares an action and passes it to <see cref="Create"/>; anything prepared against a league
/// that has since been reset is rejected at creation, canceled when the reset finishes, or
/// refused at approval, so no confirmation can act on a league it was not prepared against.
/// </remarks>
public sealed class ConfirmationGate(TimeProvider? time = null)
{
    private const int RecentLimit = 20;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ResolvedConfirmation> _recent = new();
    private readonly Lock _sync = new();
    private long _generation;

    /// <summary>Serializes approvals (and reset) so an action can never run half-way through another.</summary>
    private readonly SemaphoreSlim _execution = new(1, 1);

    private sealed record Entry(PendingConfirmation Info, Func<CancellationToken, Task<object>> Execute, long Generation);

    /// <summary>The current reset generation. Read it before preparing an action.</summary>
    public long Generation
    {
        get
        {
            lock (_sync)
            {
                return _generation;
            }
        }
    }

    public IReadOnlyList<PendingConfirmation> Pending
        => [.. _pending.Values.Select(e => e.Info).OrderBy(p => p.CreatedAtUtc)];

    public IReadOnlyList<ResolvedConfirmation> Recent
        => [.. _recent.OrderByDescending(r => r.ResolvedAtUtc)];

    /// <param name="preparedInGeneration">The <see cref="Generation"/> observed before the action was prepared.</param>
    public PendingConfirmationResult Create(
        string tool,
        string summary,
        string callerScope,
        string effectiveFranchiseId,
        IReadOnlyDictionary<string, object?> arguments,
        Func<CancellationToken, Task<object>> execute,
        long preparedInGeneration)
    {
        lock (_sync)
        {
            if (preparedInGeneration != _generation)
            {
                throw new McpException(
                    "The league was reset while this action was being prepared; nothing was queued. Ask again if it still applies.");
            }

            while (true)
            {
                var id = RandomNumberGenerator.GetHexString(8, lowercase: true);
                var info = new PendingConfirmation(
                    id, tool, summary, callerScope, effectiveFranchiseId, arguments, _time.GetUtcNow());
                if (_pending.TryAdd(id, new Entry(info, execute, _generation)))
                {
                    return new PendingConfirmationResult("pending_confirmation", id, summary);
                }
            }
        }
    }

    /// <summary>
    /// Approves (executes) or denies a pending action. Null when the id is unknown or already
    /// resolved. The entry stays pending while it executes and is removed only afterwards, with
    /// a terminal record either way.
    /// </summary>
    public async Task<ConfirmationOutcome?> ResolveAsync(string id, bool approve, CancellationToken cancellationToken)
    {
        await _execution.WaitAsync(cancellationToken);
        try
        {
            if (!_pending.TryGetValue(id, out var entry))
            {
                return null;
            }

            if (entry.Generation != Generation)
            {
                return Finish(entry, approved: false, "canceled", result: null, error: "Canceled: the league was reset after this action was queued.");
            }

            if (!approve)
            {
                return Finish(entry, approved: false, "denied", result: null, error: null);
            }

            try
            {
                var result = await entry.Execute(cancellationToken);
                return Finish(entry, approved: true, "executed", result, error: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Finish(entry, approved: true, "canceled", result: null, error: "Approval request was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                return Finish(entry, approved: true, "failed", result: null, error: Describe(ex));
            }
        }
        finally
        {
            _execution.Release();
        }
    }

    /// <summary>
    /// Demo reset. Waits for any in-flight approval (the execution lock), reseeds the league,
    /// then advances the generation and cancels every pending confirmation, including ones
    /// created while the reset was running. Returns how many were canceled.
    /// </summary>
    public async Task<int> ResetAsync(Func<CancellationToken, Task> resetLeague, CancellationToken cancellationToken)
    {
        await _execution.WaitAsync(cancellationToken);
        try
        {
            await resetLeague(cancellationToken);

            lock (_sync)
            {
                _generation++;
                while (_recent.TryDequeue(out _))
                {
                }

                var canceled = 0;
                foreach (var entry in _pending.Values.OrderBy(e => e.Info.CreatedAtUtc).ToList())
                {
                    Finish(entry, approved: false, "canceled", result: null, error: "Canceled by league reset.");
                    canceled++;
                }

                return canceled;
            }
        }
        finally
        {
            _execution.Release();
        }
    }

    private ConfirmationOutcome Finish(Entry entry, bool approved, string status, object? result, string? error)
    {
        _pending.TryRemove(entry.Info.Id, out _);
        Remember(new ResolvedConfirmation(entry.Info, approved, status, _time.GetUtcNow(), error));
        return new ConfirmationOutcome(approved, status, result, error);
    }

    /// <summary>League validation messages are safe to show; anything else is reported by type.</summary>
    private static string Describe(Exception ex)
        => ex is InvalidOperationException or KeyNotFoundException or McpException
            ? ex.Message
            : $"{ex.GetType().Name}: {ex.Message}";

    private void Remember(ResolvedConfirmation resolved)
    {
        _recent.Enqueue(resolved);
        while (_recent.Count > RecentLimit && _recent.TryDequeue(out _))
        {
        }
    }
}
