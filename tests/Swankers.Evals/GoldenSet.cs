using System.Text.Json;

namespace Swankers.Evals;

/// <summary>Loads golden/*.jsonl: one JSON object per line, lines starting with # are comments.</summary>
public static class GoldenSet
{
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "golden");

    public static IReadOnlyList<GoldenCase> Load(string? directory = null)
    {
        directory ??= DefaultDirectory;
        var cases = new List<GoldenCase>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                var parsed = JsonSerializer.Deserialize<GoldenCase>(line, GoldenCase.JsonOptions)
                    ?? throw new InvalidDataException($"{Path.GetFileName(file)}:{lineNumber}: empty case.");
                cases.Add(parsed);
            }
        }

        Validate(cases);
        return cases;
    }

    public static void Validate(IReadOnlyList<GoldenCase> cases)
    {
        if (cases.Count == 0)
        {
            throw new InvalidDataException("The golden set is empty.");
        }

        var duplicateIds = cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidDataException($"Duplicate case ids: {string.Join(", ", duplicateIds)}.");
        }

        var duplicateQueries = cases.GroupBy(c => c.Query).Where(g => g.Count() > 1).Select(g => g.First().Id).ToList();
        if (duplicateQueries.Count > 0)
        {
            throw new InvalidDataException($"Queries must be unique (they key the results): {string.Join(", ", duplicateQueries)}.");
        }

        foreach (var c in cases)
        {
            if (!GoldenCase.Categories.All.Contains(c.Category))
            {
                throw new InvalidDataException($"{c.Id}: unknown category '{c.Category}'.");
            }

            if (string.IsNullOrWhiteSpace(c.Query))
            {
                throw new InvalidDataException($"{c.Id}: empty query.");
            }

            if (c.Category == GoldenCase.Categories.InjuryCheck && c.NewsCheckFor.Count == 0)
            {
                throw new InvalidDataException($"{c.Id}: injury_check cases need newsCheckFor.");
            }

            if (c.Choices.Count > 0 && (c.Choices.Count < 2 || string.IsNullOrEmpty(c.ExpectedOutput) || !c.Choices.Contains(c.ExpectedOutput, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"{c.Id}: choices need at least two entries and expectedOutput must be one of them.");
            }
        }
    }
}
