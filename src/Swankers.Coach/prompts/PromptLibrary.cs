namespace Swankers.Coach.Prompts;

/// <summary>
/// Versioned instruction files (prompts/coach-{version}.md). v1 is the good behavior; v2 is
/// the "harmless tweak" that regresses the injury check for Thursday's rollback demo.
/// </summary>
public sealed class PromptLibrary(string promptsDirectory)
{
    private const string Prefix = "coach-";

    /// <summary>The prompts folder copied next to the binaries (see the csproj Content item).</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "prompts");

    public IReadOnlyList<string> Versions
        => !Directory.Exists(promptsDirectory)
            ? []
            : [.. Directory.EnumerateFiles(promptsDirectory, $"{Prefix}*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f)[Prefix.Length..])
                .Order(StringComparer.Ordinal)];

    public string Load(string version)
    {
        var file = Path.Combine(promptsDirectory, $"{Prefix}{version}.md");
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(
                $"No instructions for prompt version '{version}'. Available: {string.Join(", ", Versions)}.", file);
        }

        return File.ReadAllText(file);
    }
}
