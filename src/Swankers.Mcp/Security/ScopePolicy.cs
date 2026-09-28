using ModelContextProtocol;

namespace Swankers.Mcp.Security;

public enum ToolTier
{
    Read,
    Write,
    Irreversible,
}

/// <summary>
/// Decides which franchise a write acts for, from the credential's scope and the requested id.
/// This is the Friday talk's pivot: the same tools, the same request, a different credential.
/// </summary>
public static class ScopePolicy
{
    public static string ResolveEffectiveFranchise(CallerScope scope, string? requestedFranchiseId)
    {
        var requested = string.IsNullOrWhiteSpace(requestedFranchiseId) ? null : requestedFranchiseId.Trim();

        switch (scope.Kind)
        {
            case ScopeKind.Owner:
                if (requested is null || requested == scope.FranchiseId)
                {
                    return scope.FranchiseId;
                }

                throw new ScopeViolationException(scope, requested);

            case ScopeKind.Commissioner:
                // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
                // A commissioner session acts for any franchise, including "0000", as MFL does.
                return requested ?? scope.FranchiseId;

            default:
                throw new InvalidOperationException($"Unknown scope kind {scope.Kind}.");
        }
    }
}

/// <summary>
/// Raised when an owner credential names another franchise. Derives from McpException so the
/// model sees the reason and can stop; the message contains no secrets.
/// </summary>
public sealed class ScopeViolationException(CallerScope scope, string requestedFranchiseId)
    : McpException(
        $"Scope denied: this credential is the owner of franchise {scope.FranchiseId} " +
        $"and cannot act for franchise {requestedFranchiseId}.")
{
    public string RequestedFranchiseId { get; } = requestedFranchiseId;
}
