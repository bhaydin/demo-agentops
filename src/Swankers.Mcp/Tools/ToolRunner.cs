using System.Diagnostics;
using ModelContextProtocol;
using Swankers.Mcp.Gate;
using Swankers.Mcp.Security;

namespace Swankers.Mcp.Tools;

/// <summary>Per-call context handed to a tool body after scope resolution.</summary>
public sealed class ToolContext(CallerScope scope, string effectiveFranchiseId, Activity? activity)
{
    public CallerScope Scope { get; } = scope;

    /// <summary>The franchise the call reads or acts on. Starts as the scope decision; tools narrow it.</summary>
    public string EffectiveFranchiseId { get; private set; } = effectiveFranchiseId;

    public Activity? Activity { get; } = activity;

    /// <summary>executed | pending | gate_off | denied_scope | error</summary>
    public string Decision { get; set; } = "executed";

    /// <summary>Records the concrete franchise affected (e.g. a cross-franchise read, or the roster a "0000" drop resolved to).</summary>
    public void SetEffective(string franchiseId)
    {
        EffectiveFranchiseId = franchiseId;
        Activity?.SetTag(McpDiagnostics.EffectiveFranchiseTag, franchiseId);
    }
}

/// <summary>
/// An irreversible action after its target has been resolved exactly once. The gate stores
/// <paramref name="TargetFranchiseId"/> and <paramref name="Summary"/>; <paramref name="Execute"/>
/// acts on that same target and re-validates preconditions instead of resolving again.
/// </summary>
public sealed record PreparedAction(
    string TargetFranchiseId,
    string Summary,
    Func<CancellationToken, Task<object>> Execute);

/// <summary>
/// Runs every tool call the same way: one span, scope resolved from the credential, the gate
/// applied to irreversible tiers, and league errors turned into model-visible McpExceptions.
/// </summary>
public sealed class ToolRunner(
    CallerScopeAccessor scopeAccessor,
    ConfirmationGate gate,
    ILogger<ToolRunner> logger)
{
    public async Task<T> RunAsync<T>(
        string tool,
        ToolTier tier,
        string? requestedFranchiseId,
        Func<ToolContext, Task<T>> body,
        CancellationToken cancellationToken)
    {
        using var activity = McpDiagnostics.ActivitySource.StartActivity($"mcp.tool {tool}");
        var scope = scopeAccessor.Current;
        activity?.SetTag(McpDiagnostics.ToolNameTag, tool);
        activity?.SetTag(McpDiagnostics.TierTag, tier.ToString());
        activity?.SetTag(McpDiagnostics.CallerScopeTag, scope.Label);
        activity?.SetTag(McpDiagnostics.RequestedFranchiseTag, requestedFranchiseId ?? "");

        try
        {
            var effective = ScopePolicy.ResolveEffectiveFranchise(scope, requestedFranchiseId);
            var context = new ToolContext(scope, effective, activity);
            context.SetEffective(effective);

            var result = await body(context);
            activity?.SetTag(McpDiagnostics.GateDecisionTag, context.Decision);
            return result;
        }
        catch (ScopeViolationException)
        {
            activity?.SetTag(McpDiagnostics.GateDecisionTag, "denied_scope");
            logger.LogWarning("{Tool}: scope denied for {Scope} requesting franchise {Requested}.",
                tool, scope.Label, requestedFranchiseId);
            throw;
        }
        catch (McpException)
        {
            activity?.SetTag(McpDiagnostics.GateDecisionTag, "error");
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            // League validation messages are safe and useful to the model.
            activity?.SetTag(McpDiagnostics.GateDecisionTag, "error");
            throw new McpException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Irreversible tools: <paramref name="prepare"/> resolves the concrete target once. With the
    /// caller's gate on, the prepared action is parked and only the REST approval endpoint runs
    /// it; with the gate off it runs now.
    /// </summary>
    public Task<object> RunIrreversibleAsync(
        string tool,
        string? requestedFranchiseId,
        IReadOnlyDictionary<string, object?> arguments,
        Func<ToolContext, Task<PreparedAction>> prepare,
        CancellationToken cancellationToken)
        => RunAsync<object>(tool, ToolTier.Irreversible, requestedFranchiseId, async context =>
        {
            // Snapshot the reset generation first: if the league is reset while we prepare,
            // the gate refuses the confirmation instead of queuing a stale action.
            var generation = gate.Generation;
            var action = await prepare(context);
            context.SetEffective(action.TargetFranchiseId);

            if (context.Scope.GateEnabled)
            {
                context.Decision = "pending";
                return gate.Create(
                    tool, action.Summary, context.Scope.Label, action.TargetFranchiseId, arguments, action.Execute, generation);
            }

            // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
            // Gate off: the irreversible action runs immediately, with no human in the loop.
            context.Decision = "gate_off";
            return await action.Execute(cancellationToken);
        }, cancellationToken);
}
