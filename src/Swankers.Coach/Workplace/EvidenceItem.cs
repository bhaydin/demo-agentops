using System.Globalization;
using System.Text.RegularExpressions;

namespace Swankers.Coach.Workplace;

/// <summary>
/// One piece of evidence the workplace scenario hands to the model. <see cref="SourceRef"/> and
/// <see cref="SourceUrl"/> are whatever the provider returned, unchanged, and null when it
/// returned none. <see cref="Synthetic"/> marks fixture content that never came from Microsoft
/// 365; <see cref="Synthesized"/> marks a Work IQ natural-language answer, which is not source text.
/// <para>
/// Two dates, with different meanings. <see cref="BusinessDate"/> is the date the evidence itself
/// states, and it is the only chronology the scenario uses. <see cref="ReceivedAt"/> is when the
/// mailbox received the message: transport metadata, which for a seeded corpus is the day it was
/// seeded and says nothing about when the event happened.
/// </para>
/// </summary>
public sealed record EvidenceItem(
    string Id,
    string SourceType,
    string? Title,
    string? Author,
    DateTimeOffset? ReceivedAt,
    string Text,
    string? SourceRef,
    string? SourceUrl,
    bool Synthetic = false,
    bool Synthesized = false)
{
    /// <summary>
    /// The first date written in <see cref="Text"/>, or null when it states none. Derived the
    /// same way for Live and Replay, and never filled in from <see cref="ReceivedAt"/>.
    /// </summary>
    public DateOnly? BusinessDate => EvidenceDates.FirstStatedDate(Text);
}

/// <summary>
/// Reads the business date an evidence item states. Deliberately narrow: the corpus convention is
/// that each item opens with its date written out, as in "September 30, 2026", and that is the
/// only form recognized.
/// </summary>
public static partial class EvidenceDates
{
    public static DateOnly? FirstStatedDate(string text)
    {
        foreach (Match match in WrittenDate().Matches(text))
        {
            if (DateOnly.TryParseExact(
                $"{match.Groups["month"].Value} {match.Groups["day"].Value}, {match.Groups["year"].Value}",
                "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\b(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<day>\d{1,2}),\s+(?<year>\d{4})\b")]
    private static partial Regex WrittenDate();
}
