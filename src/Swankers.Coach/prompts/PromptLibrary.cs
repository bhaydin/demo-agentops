namespace Swankers.Coach.Prompts;

/// <summary>
/// Versioned instruction files (prompts/{prefix}{version}.md). For the Coach, v1 is the good
/// behavior and v2 is the "harmless tweak" that regresses the injury check for Thursday's rollback
/// demo. The workplace scenario keeps its own versions under its own prefix, so neither set can be
/// loaded, listed, or counted as the other.
/// </summary>
public sealed class PromptLibrary(string promptsDirectory, string prefix = PromptLibrary.CoachPrefix)
{
    public const string CoachPrefix = "coach-";
    public const string MeetingPrepPrefix = "meetingprep-";

    /// <summary>The prompts folder copied next to the binaries (see the csproj Content item).</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "prompts");

    public IReadOnlyList<string> Versions
        => !Directory.Exists(promptsDirectory)
            ? []
            : [.. Directory.EnumerateFiles(promptsDirectory, $"{prefix}*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f)[prefix.Length..])
                .Order(StringComparer.Ordinal)];

    public string Load(string version)
    {
        var file = Path.Combine(promptsDirectory, $"{prefix}{version}.md");
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(
                $"No instructions for prompt version '{version}'. Available: {string.Join(", ", Versions)}.", file);
        }

        return File.ReadAllText(file);
    }
}
