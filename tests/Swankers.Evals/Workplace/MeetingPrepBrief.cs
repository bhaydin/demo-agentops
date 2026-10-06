using System.Text.RegularExpressions;

namespace Swankers.Evals.Workplace;

/// <summary>A bullet of the brief and the evidence ids it cites in square brackets.</summary>
public sealed record BriefBullet(string Text, IReadOnlyList<string> Citations);

/// <summary>
/// The agent's answer read as the five-section brief its instructions ask for. Parsing never
/// fails: a missing heading simply has no bullets, and the structure check reports it.
/// </summary>
public sealed partial class MeetingPrepBrief
{
    public const string Decisions = "Confirmed decisions";
    public const string Commitments = "Open commitments";
    public const string Risks = "Unresolved risks";
    public const string Missing = "Missing, conflicting, or stale information";
    public const string Sources = "Sources";

    public static readonly IReadOnlyList<string> Headings = [Decisions, Commitments, Risks, Missing, Sources];

    private readonly Dictionary<string, List<BriefBullet>> _sections;

    private MeetingPrepBrief(string openingLine, IReadOnlyList<string> headingsFound, Dictionary<string, List<BriefBullet>> sections)
    {
        OpeningLine = openingLine;
        HeadingsFound = headingsFound;
        _sections = sections;
    }

    /// <summary>The first non-empty line, which should state the reference time, engagement, and evidence mode.</summary>
    public string OpeningLine { get; }

    /// <summary>The recognized headings, in the order they appear.</summary>
    public IReadOnlyList<string> HeadingsFound { get; }

    public IReadOnlyList<BriefBullet> Bullets(string heading)
        => _sections.TryGetValue(heading, out var bullets) ? bullets : [];

    /// <summary>Every id cited in the four content sections (not the Sources list).</summary>
    public IReadOnlyList<string> CitedInContent
        => [.. Headings.Where(h => h != Sources).SelectMany(Bullets).SelectMany(b => b.Citations).Distinct(StringComparer.Ordinal)];

    /// <summary>The id each Sources line is for (the first id on the line), with the line.</summary>
    public IReadOnlyList<(string Id, string Line)> SourceLines
        => [.. Bullets(Sources).Select(b => (Id: EvidenceId().Match(b.Text).Value, Line: b.Text)).Where(s => s.Id.Length > 0)];

    public static MeetingPrepBrief Parse(string answer)
    {
        var lines = answer.ReplaceLineEndings("\n").Split('\n');
        var opening = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        var found = new List<string>();
        var sections = new Dictionary<string, List<BriefBullet>>(StringComparer.Ordinal);
        List<BriefBullet>? current = null;
        var isSources = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var heading = HeadingLine().Match(line);
            if (heading.Success)
            {
                var name = Headings.FirstOrDefault(h => h.Equals(heading.Groups["name"].Value.Trim().TrimEnd(':'), StringComparison.OrdinalIgnoreCase));
                current = null;
                if (name is not null)
                {
                    found.Add(name);
                    isSources = name == Sources;
                    if (!sections.TryGetValue(name, out current))
                    {
                        sections[name] = current = [];
                    }
                }

                continue;
            }

            if (current is null || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var bullet = BulletLine().Match(line);
            if (bullet.Success)
            {
                var text = bullet.Groups["text"].Value.Trim();
                if (!NothingToReport().IsMatch(text))
                {
                    current.Add(Bullet(text, isSources));
                }
            }
            else if (current.Count > 0 && char.IsWhiteSpace(raw, 0))
            {
                // A wrapped line belongs to the bullet above it.
                current[^1] = Bullet(current[^1].Text + " " + line.Trim(), isSources);
            }
        }

        return new MeetingPrepBrief(opening, found, sections);
    }

    private static BriefBullet Bullet(string text, bool isSources)
        => new(text, isSources ? [] : [.. Bracketed().Matches(text).SelectMany(m => EvidenceId().Matches(m.Value)).Select(m => m.Value).Distinct(StringComparer.Ordinal)]);

    [GeneratedRegex(@"^\s{0,3}#{1,4}\s+(?<name>.+?)\s*#*\s*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)])\s+(?<text>.+)$")]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^none in the evidence\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex NothingToReport();

    [GeneratedRegex(@"\[[^\[\]]*\]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"\bE\d+\b")]
    private static partial Regex EvidenceId();
}
