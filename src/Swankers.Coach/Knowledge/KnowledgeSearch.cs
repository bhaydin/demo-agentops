using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Swankers.Coach.Knowledge;

public sealed record KnowledgeHit(string Source, string Heading, string Text);

/// <summary>
/// The search_league_knowledge tool: lexical search over the markdown files in knowledge/,
/// split into heading sections. Deliberately simple and local; the content is real league
/// documentation. (Foundry IQ grounding is a stretch item; see docs/BUILD-PLAN.md Phase 3.)
/// </summary>
public sealed partial class KnowledgeSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "does", "for", "from", "how", "i",
        "in", "is", "it", "many", "my", "of", "on", "or", "our", "the", "this", "to", "what", "when",
        "which", "who", "with", "you", "your",
    };

    private readonly IReadOnlyList<Section> _sections;

    private sealed record Section(string Source, string Heading, string Text, IReadOnlySet<string> Terms, IReadOnlySet<string> HeadingTerms);

    private KnowledgeSearch(IReadOnlyList<Section> sections) => _sections = sections;

    public int SectionCount => _sections.Count;

    public static KnowledgeSearch Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Knowledge folder not found: {directory}");
        }

        var sections = new List<Section>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).Equals("README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sections.AddRange(Split(Path.GetFileName(file), File.ReadAllText(file)));
        }

        return new KnowledgeSearch(sections);
    }

    /// <summary>Best-matching sections for a question; empty when nothing overlaps.</summary>
    public IReadOnlyList<KnowledgeHit> Search(string query, int top = 3)
    {
        var terms = Tokenize(query).Where(t => !StopWords.Contains(t)).Distinct().ToList();
        if (terms.Count == 0)
        {
            return [];
        }

        return
        [
            .. _sections
                .Select(s => (Section: s, Score: terms.Sum(t => (s.Terms.Contains(t) ? 1.0 : 0.0) + (s.HeadingTerms.Contains(t) ? 1.5 : 0.0))))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Section.Text.Length)
                .Take(top)
                .Select(x => new KnowledgeHit(x.Section.Source, x.Section.Heading, x.Section.Text))
        ];
    }

    [Description("Look up Swankers league rules and notes: scoring, roster and lineup rules, trades, waivers, the owner's draft notes, and the weekly digest. Returns the most relevant sections with their source file.")]
    public IReadOnlyList<KnowledgeHit> SearchLeagueKnowledge(
        [Description("What to look up, e.g. \"points per reception\" or \"trade deadline\"")] string query)
        => Search(query);

    public AITool AsTool()
        => AIFunctionFactory.Create(SearchLeagueKnowledge, name: "search_league_knowledge");

    private static IEnumerable<Section> Split(string source, string markdown)
    {
        var heading = Path.GetFileNameWithoutExtension(source);
        var body = new List<string>();
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith('#'))
            {
                if (body.Count > 0)
                {
                    yield return Make(source, heading, body);
                    body = [];
                }

                heading = trimmed.TrimStart('#').Trim();
                continue;
            }

            if (!trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                body.Add(trimmed);
            }
        }

        if (body.Count > 0)
        {
            yield return Make(source, heading, body);
        }
    }

    private static Section Make(string source, string heading, List<string> body)
    {
        var text = string.Join("\n", body).Trim();
        return new Section(
            source, heading, text,
            Tokenize(text).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Tokenize(heading).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Tokenize(string text)
        => TokenPattern().Matches(text).Select(m => m.Value.ToLowerInvariant());

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9']+|\d+")]
    private static partial Regex TokenPattern();
}
