using System.Diagnostics;

namespace Swankers.AgentDeploy;

/// <summary>
/// Produces the code bundle Foundry runs: a framework-dependent <c>dotnet publish</c> of
/// Swankers.Coach (prompts and knowledge included by its project file). The publish is
/// portable (no runtime identifier) so it never rewrites the project's lock file.
/// The output folder is replaced on every publish, so it must be one this tool owns:
/// never the checkout, anything inside or above it, or a folder someone else filled.
/// </summary>
public static class CoachPublisher
{
    private const string RepoMarker = "SwankersCoach.slnx";

    /// <summary>Written into every output folder this tool creates; only such folders are ever deleted.</summary>
    public const string OwnershipMarker = ".swankers-coach-publish";

    /// <summary>Default bundle location: outside the checkout, whose path may contain characters MSBuild rejects.</summary>
    public static string DefaultOutputDirectory => Path.Combine(Path.GetTempPath(), "swankers-coach-publish");

    public static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoMarker)))
            {
                return dir.FullName;
            }
        }

        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoMarker)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Run from inside the repository ({RepoMarker} not found).");
    }

    public static async Task PublishAsync(string repoRoot, string outputDirectory, CancellationToken ct)
    {
        var output = PrepareOutputDirectory(outputDirectory, repoRoot);

        var project = Path.Combine(repoRoot, "src", "Swankers.Coach", "Swankers.Coach.csproj");
        var start = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", project, "-c", "Release", "-o", output, "-nologo" },
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish failed with exit code {process.ExitCode}.");
        }

        Verify(output);
    }

    /// <summary>
    /// Validates the destination and empties it. Refuses a drive or filesystem root, a path that
    /// MSBuild cannot take, and anything that is, contains, or lies inside the checkout. An
    /// existing folder is deleted only when it is empty or carries <see cref="OwnershipMarker"/>;
    /// anything else is left untouched with an error.
    /// </summary>
    public static string PrepareOutputDirectory(string outputDirectory, string repoRoot)
    {
        var output = Normalize(outputDirectory);
        var repo = Normalize(repoRoot);

        // `dotnet publish -o` becomes an MSBuild property, and MSBuild splits property values on
        // ',' and ';' (MSB1006). A OneDrive folder such as "Contoso, Inc" trips this.
        if (output.Contains(',') || output.Contains(';'))
        {
            throw new ArgumentException(
                $"Publish output path '{output}' contains ',' or ';', which dotnet publish -o cannot pass to MSBuild. Use --output with another folder (default: {DefaultOutputDirectory}).");
        }

        if (string.Equals(Path.GetPathRoot(output), output, PathComparison))
        {
            throw new ArgumentException($"Refusing to publish into the root '{output}'.");
        }

        if (IsSameOrInside(repo, output) || IsSameOrInside(output, repo))
        {
            throw new ArgumentException(
                $"Publish output '{output}' is the checkout, lies inside it, or contains it. Publish outside the repository (default: {DefaultOutputDirectory}).");
        }

        if (Directory.Exists(output))
        {
            var owned = File.Exists(Path.Combine(output, OwnershipMarker));
            if (!owned && Directory.EnumerateFileSystemEntries(output).Any())
            {
                throw new InvalidOperationException(
                    $"Publish output '{output}' already has content that this tool did not create; nothing was deleted. Choose an empty folder or one this tool published to before.");
            }

            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
        File.WriteAllText(
            Path.Combine(output, OwnershipMarker),
            "Created by tools/Swankers.AgentDeploy. Everything in this folder is replaced on the next publish.\n");
        return output;
    }

    /// <summary>The bundle must be flat and self-describing: entry assembly, runtime config, prompts, knowledge.</summary>
    public static void Verify(string outputDirectory)
    {
        string[] required =
        [
            "Swankers.Coach.dll",
            "Swankers.Coach.runtimeconfig.json",
            Path.Combine("prompts", "coach-v1.md"),
            Path.Combine("prompts", "coach-v2.md"),
        ];

        foreach (var file in required)
        {
            if (!File.Exists(Path.Combine(outputDirectory, file)))
            {
                throw new InvalidOperationException($"Publish output is missing {file} under {outputDirectory}.");
            }
        }

        var knowledge = Path.Combine(outputDirectory, "knowledge");
        if (!Directory.Exists(knowledge) || !Directory.EnumerateFiles(knowledge, "*.md").Any())
        {
            throw new InvalidOperationException($"Publish output has no knowledge/*.md under {outputDirectory}.");
        }
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string ancestor, string path)
        => string.Equals(ancestor, path, PathComparison)
        || path.StartsWith(ancestor + Path.DirectorySeparatorChar, PathComparison)
        || path.StartsWith(ancestor + Path.AltDirectorySeparatorChar, PathComparison);
}
