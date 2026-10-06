using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Swankers.Evals.Workplace;

/// <summary>
/// One WorkplaceMeetingPrep case: a question, and what an answer to that question must contain.
/// What may never appear (a suggestion as a decision, a completed item as open) is not per case;
/// it follows from <see cref="WorkplaceAnswerKey"/> and is checked on every answer.
/// </summary>
public sealed record WorkplaceCase
{
    public required string Id { get; init; }

    public required string Query { get; init; }

    /// <summary>False for a request the agent must decline, where a plain reply is acceptable and the five-section brief is not required.</summary>
    public bool BriefExpected { get; init; } = true;

    /// <summary>Evidence ids that must be cited under Confirmed decisions.</summary>
    public IReadOnlyList<string> RequireDecisions { get; init; } = [];

    /// <summary>Evidence ids that must be cited under Open commitments.</summary>
    public IReadOnlyList<string> RequireCommitments { get; init; } = [];

    /// <summary>Evidence ids that must be cited under Unresolved risks.</summary>
    public IReadOnlyList<string> RequireRisks { get; init; } = [];

    /// <summary>Evidence ids that must be cited under Missing, conflicting, or stale information.</summary>
    public IReadOnlyList<string> RequireMissing { get; init; } = [];

    /// <summary>Statements about the answer that the judge must find true.</summary>
    public IReadOnlyList<string> JudgedClaims { get; init; } = [];

    public string? Notes { get; init; }

    public IEnumerable<string> ReferencedIds => RequireDecisions.Concat(RequireCommitments).Concat(RequireRisks).Concat(RequireMissing);
}

/// <summary>What each evidence item of a fixture is; the placement rules are derived from it.</summary>
public sealed record WorkplaceAnswerKey
{
    public required string Fixture { get; init; }

    public IReadOnlyList<string> Decisions { get; init; } = [];

    public IReadOnlyList<Supersession> Superseded { get; init; } = [];

    public IReadOnlyList<string> Suggestions { get; init; } = [];

    public IReadOnlyList<Commitment> OpenCommitments { get; init; } = [];

    public IReadOnlyList<string> Completed { get; init; } = [];

    public IReadOnlyList<string> Risks { get; init; } = [];

    public IReadOnlyList<string> Unanswered { get; init; } = [];

    public IEnumerable<string> AllIds
        => Decisions.Concat(Superseded.Select(s => s.Id)).Concat(Suggestions).Concat(OpenCommitments.Select(c => c.Id))
            .Concat(Completed).Concat(Risks).Concat(Unanswered);

    /// <summary>An item whose position was replaced by a later one.</summary>
    public sealed record Supersession(string Id, string By);

    /// <summary>An open commitment and the owner its evidence names.</summary>
    public sealed record Commitment(string Id, string Owner);
}

/// <summary>Loads the workplace cases and answer key that ship in workplace-golden/, apart from the football golden set.</summary>
public static class WorkplaceCaseSet
{
    public const string CasesFile = "meeting-prep.jsonl";
    public const string KeyFile = "northwind-v1.key.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "workplace-golden");

    public static IReadOnlyList<WorkplaceCase> LoadCases(string? directory = null)
    {
        var path = Path.Combine(directory ?? DefaultDirectory, CasesFile);
        var cases = new List<WorkplaceCase>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            cases.Add(JsonSerializer.Deserialize<WorkplaceCase>(line, JsonOptions)
                ?? throw new InvalidDataException($"{CasesFile}:{lineNumber}: empty case."));
        }

        return cases;
    }

    public static WorkplaceAnswerKey LoadKey(string? directory = null)
        => JsonSerializer.Deserialize<WorkplaceAnswerKey>(File.ReadAllText(Path.Combine(directory ?? DefaultDirectory, KeyFile)), JsonOptions)
            ?? throw new InvalidDataException($"{KeyFile} is empty.");

    public static void Validate(IReadOnlyList<WorkplaceCase> cases, WorkplaceAnswerKey key)
    {
        if (cases.Count == 0)
        {
            throw new InvalidDataException("The workplace case set is empty.");
        }

        var duplicates = cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key)
            .Concat(cases.GroupBy(c => c.Query).Where(g => g.Count() > 1).Select(g => g.First().Id)).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidDataException($"Case ids and queries must be unique: {string.Join(", ", duplicates)}.");
        }

        var known = key.AllIds.ToHashSet(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Query))
            {
                throw new InvalidDataException($"{c.Id}: empty query.");
            }

            var unknown = c.ReferencedIds.Where(id => !known.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidDataException($"{c.Id}: evidence ids not in the {key.Fixture} answer key: {string.Join(", ", unknown)}.");
            }

            var misplaced = c.RequireDecisions.Except(key.Decisions)
                .Concat(c.RequireCommitments.Except(key.OpenCommitments.Select(o => o.Id)))
                .Concat(c.RequireRisks.Except(key.Risks)).ToList();
            if (misplaced.Count > 0)
            {
                throw new InvalidDataException($"{c.Id}: requires ids in a section the answer key does not allow them in: {string.Join(", ", misplaced)}.");
            }
        }
    }

    /// <summary>SHA-256 of a file with line endings normalized, so it is the same on every checkout.</summary>
    public static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).ReplaceLineEndings("\n"))));
}
