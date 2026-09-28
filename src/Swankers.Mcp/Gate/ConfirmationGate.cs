using System.Collections.Concurrent;
using System.Security.Cryptography;

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

public sealed record ResolvedConfirmation(
    PendingConfirmation Confirmation,
    bool Approved,
    DateTimeOffset ResolvedAtUtc,
    string? Error);

public sealed record ConfirmationOutcome(bool Approved, object? Result, string? Error);

/// <summary>What the agent gets back instead of a result when the gate holds an action.</summary>
public sealed record PendingConfirmationResult(string Status, string ConfirmationId, string Summary);

/// <summary>
/// Holds irreversible actions until a human approves or denies them through the REST endpoint.
/// Approval is deliberately not an MCP tool: nothing here is reachable from the agent except
/// <see cref="Create"/>, so the agent cannot approve its own actions.
/// </summary>
public sealed class ConfirmationGate(TimeProvider? time = null)
{
    private const int RecentLimit = 20;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ResolvedConfirmation> _recent = new();

    private sealed record Entry(PendingConfirmation Info, Func<CancellationToken, Task<object>> Execute);

    public IReadOnlyList<PendingConfirmation> Pending
        => [.. _pending.Values.Select(e => e.Info).OrderBy(p => p.CreatedAtUtc)];

    public IReadOnlyList<ResolvedConfirmation> Recent
        => [.. _recent.OrderByDescending(r => r.ResolvedAtUtc)];

    public PendingConfirmationResult Create(
        string tool,
        string summary,
        string callerScope,
        string effectiveFranchiseId,
        IReadOnlyDictionary<string, object?> arguments,
        Func<CancellationToken, Task<object>> execute)
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetHexString(8, lowercase: true);
            var info = new PendingConfirmation(
                id, tool, summary, callerScope, effectiveFranchiseId, arguments, _time.GetUtcNow());
            if (_pending.TryAdd(id, new Entry(info, execute)))
            {
                return new PendingConfirmationResult("pending_confirmation", id, summary);
            }
        }
    }

    /// <summary>Approves (executes) or denies a pending action. Null when the id is unknown.</summary>
    public async Task<ConfirmationOutcome?> ResolveAsync(string id, bool approve, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(id, out var entry))
        {
            return null;
        }

        object? result = null;
        string? error = null;
        if (approve)
        {
            try
            {
                result = await entry.Execute(cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                error = ex.Message;
            }
        }

        Remember(new ResolvedConfirmation(entry.Info, approve, _time.GetUtcNow(), error));
        return new ConfirmationOutcome(approve, result, error);
    }

    /// <summary>Drops every pending and recent confirmation (demo reset).</summary>
    public void Clear()
    {
        _pending.Clear();
        while (_recent.TryDequeue(out _))
        {
        }
    }

    private void Remember(ResolvedConfirmation resolved)
    {
        _recent.Enqueue(resolved);
        while (_recent.Count > RecentLimit && _recent.TryDequeue(out _))
        {
        }
    }
}
