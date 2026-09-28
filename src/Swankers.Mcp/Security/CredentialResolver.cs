using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Swankers.Mcp.Security;

/// <summary>
/// Maps the bearer credential on an MCP request to a <see cref="CallerScope"/>. Comparison is
/// constant-time; unknown or missing credentials resolve to null (the middleware answers 401).
/// </summary>
public sealed class CredentialResolver(IOptions<McpOptions> options)
{
    public CallerScope? Resolve(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var presented = header["Bearer ".Length..].Trim();
        if (presented.Length == 0)
        {
            return null;
        }

        var o = options.Value;
        if (Matches(presented, o.OwnerCredential))
        {
            return new CallerScope(ScopeKind.Owner, o.OwnerFranchiseId, o.OwnerGateEnabled);
        }

        // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
        // The commissioner credential carries league-wide scope; ScopePolicy honors any
        // franchiseId for it, including "0000", exactly as MFL does for a commissioner session.
        if (Matches(presented, o.CommissionerCredential))
        {
            return new CallerScope(ScopeKind.Commissioner, o.OwnerFranchiseId, o.CommissionerGateEnabled);
        }

        return null;
    }

    private static bool Matches(string presented, string expected)
        => expected.Length > 0 &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(presented),
               Encoding.UTF8.GetBytes(expected));
}
