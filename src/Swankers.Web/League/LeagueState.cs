using System.Text.Json;

namespace Swankers.Web.League;

// The shapes Swankers.Mcp's GET /api/state returns (Swankers.Mcp.Tools.LeagueStateView and the
// gate records), as the ticker reads them. Kept as plain records here so the web app depends on
// the REST contract, not on the MCP server project.

public sealed record PlayerInfo(string Id, string Name, string Position, string Team, string? RosterStatus);

public sealed record FranchiseState(string Id, string Name, bool IsSimOnly, IReadOnlyList<PlayerInfo> Roster);

public sealed record TransactionEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Type,
    string FranchiseId,
    string FranchiseName,
    string Description,
    IReadOnlyList<string> PlayerNames);

public sealed record TradeOffer(
    string Id,
    string FromFranchiseId,
    string FromFranchiseName,
    string ToFranchiseId,
    string ToFranchiseName,
    IReadOnlyList<PlayerInfo> Give,
    IReadOnlyList<PlayerInfo> Get,
    string Note,
    string Status,
    DateTimeOffset OfferedOn);

/// <summary>An irreversible action the gate is holding for a human decision.</summary>
public sealed record PendingApproval(
    string Id,
    string Tool,
    string Summary,
    string CallerScope,
    string EffectiveFranchiseId,
    JsonElement Arguments,
    DateTimeOffset CreatedAtUtc);

/// <summary><paramref name="Status"/> is executed, denied, failed, or canceled.</summary>
public sealed record ResolvedApproval(PendingApproval Confirmation, bool Approved, string Status, DateTimeOffset ResolvedAtUtc, string? Error);

public sealed record GateState(bool OwnerGateEnabled, bool CommissionerGateEnabled);

/// <param name="Gate">Null when the server does not report it (an MCP build older than the web app).</param>
public sealed record LeagueState(
    int Week,
    string OwnerFranchiseId,
    IReadOnlyList<FranchiseState> Franchises,
    IReadOnlyList<TransactionEntry> Transactions,
    IReadOnlyList<TradeOffer> PendingTrades,
    IReadOnlyList<PendingApproval> PendingConfirmations,
    IReadOnlyList<ResolvedApproval> RecentConfirmations,
    GateState? Gate)
{
    public static readonly LeagueState Empty = new(0, "", [], [], [], [], [], null);

    /// <summary>The franchise the owner credential acts for (Brian's, highlighted in the ticker).</summary>
    public FranchiseState? Owner => Franchises.FirstOrDefault(f => f.Id == OwnerFranchiseId);
}

/// <summary>What POST /api/confirmations/{id} returns.</summary>
public sealed record ApprovalOutcome(bool Approved, string Status, JsonElement? Result, string? Error);
