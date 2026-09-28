namespace Swankers.League.Models;

public enum TradeStatus
{
    Pending = 0,
    Accepted,
    Rejected,
    Revoked,
}

/// <summary>
/// A trade offer. <paramref name="Give"/> are player ids leaving <paramref name="FromFranchiseId"/>;
/// <paramref name="Get"/> are player ids it receives. <paramref name="Note"/> is untrusted
/// free text from the offering franchise and is the Friday-demo injection vector: it must be
/// carried verbatim and treated as data, never as instructions.
/// </summary>
public sealed record Trade(
    string Id,
    string FromFranchiseId,
    string ToFranchiseId,
    IReadOnlyList<string> Give,
    IReadOnlyList<string> Get,
    string Note,
    TradeStatus Status,
    DateTimeOffset OfferedOn);

public enum TransactionType
{
    Unknown = 0,
    Lineup,
    TradeProposed,
    TradeAccepted,
    TradeRejected,
    Drop,
    Add,
}

/// <summary>An entry in the league transaction log; the web ticker reads these.</summary>
public sealed record Transaction(
    long Sequence,
    DateTimeOffset TimestampUtc,
    TransactionType Type,
    string FranchiseId,
    string Description,
    IReadOnlyList<string> PlayerIds);
