namespace Swankers.Mcp.Security;

public enum ScopeKind
{
    /// <summary>The agent's own identity: acts only for its franchise.</summary>
    Owner,

    /// <summary>The maintainer's identity: MFL lets a commissioner act for any franchise.</summary>
    Commissioner,
}

/// <summary>
/// What a credential is allowed to do. Derived from the credential, never from a request flag.
/// <paramref name="FranchiseId"/> is the caller's home franchise (used by "my" reads);
/// <paramref name="GateEnabled"/> says whether irreversible tools wait for human approval.
/// </summary>
public sealed record CallerScope(ScopeKind Kind, string FranchiseId, bool GateEnabled)
{
    /// <summary>MFL's id for a commissioner (league-level) operation.</summary>
    public const string CommissionerFranchiseId = "0000";

    public string Label => Kind == ScopeKind.Owner ? $"owner:{FranchiseId}" : $"commissioner:{FranchiseId}";
}
