using System.Diagnostics;
using ModelContextProtocol;
using Swankers.Mcp.Gate;
using Swankers.Mcp.Security;

namespace Swankers.Mcp.Tools;

/// <summary>Per-call context handed to a tool body after scope resolution.</summary>
public sealed class ToolContext(CallerScope scope, string effectiveFranchiseId, Activity? activity)
{
    public CallerScope Scope { get; } = scope;

    public string EffectiveFranchiseId { get; } = effectiveFranchiseId;

    public Activity? Activity { get; } = activity;

    /// <summary>executed | pending | gate_off | denied_scope | error</summary>
    public string Decision { get; set; } = "executed";
}

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

        ToolContext? context = null;
        try
        {
            var effective = ScopePolicy.ResolveEffectiveFranchise(scope, requestedFranchiseId);
            activity?.SetTag(McpDiagnostics.EffectiveFranchiseTag, effective);
            context = new ToolContext(scope, effective, activity);

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
    /// Irreversible tools: when the caller's gate is on, park the action and return a pending
    /// confirmation; only the REST approval endpoint can run it. When the gate is off, run it now.
    /// </summary>
    public Task<object> RunIrreversibleAsync(
        string tool,
        string? requestedFranchiseId,
        IReadOnlyDictionary<string, object?> arguments,
        Func<ToolContext, Task<string>> summarize,
        Func<ToolContext, CancellationToken, Task<object>> execute,
        CancellationToken cancellationToken)
        => RunAsync<object>(tool, ToolTier.Irreversible, requestedFranchiseId, async context =>
        {
            var summary = await summarize(context);
            if (context.Scope.GateEnabled)
            {
                context.Decision = "pending";
                return gate.Create(
                    tool, summary, context.Scope.Label, context.EffectiveFranchiseId, arguments,
                    ct => execute(context, ct));
            }

            // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
            // Gate off: the irreversible action runs immediately, with no human in the loop.
            context.Decision = "gate_off";
            return await execute(context, cancellationToken);
        }, cancellationToken);
}
