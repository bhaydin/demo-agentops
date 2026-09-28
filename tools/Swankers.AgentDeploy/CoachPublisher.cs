using System.Diagnostics;

namespace Swankers.AgentDeploy;

/// <summary>
/// Produces the code bundle Foundry runs: a framework-dependent <c>dotnet publish</c> of
/// Swankers.Coach (prompts and knowledge included by its project file). The publish is
/// portable (no runtime identifier) so it never rewrites the project's lock file.
/// </summary>
public static class CoachPublisher
{
    private const string RepoMarker = "SwankersCoach.slnx";

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

    /// <summary>Default bundle location: outside the checkout, whose path may contain characters MSBuild rejects.</summary>
    public static string DefaultOutputDirectory => Path.Combine(Path.GetTempPath(), "swankers-coach-publish");

    public static async Task PublishAsync(string repoRoot, string outputDirectory, CancellationToken ct)
    {
        // `dotnet publish -o` becomes an MSBuild property, and MSBuild splits property values on
        // ',' and ';' (MSB1006). A OneDrive folder such as "Contoso, Inc" trips this.
        if (outputDirectory.Contains(',') || outputDirectory.Contains(';'))
        {
            throw new ArgumentException(
                $"Publish output path '{outputDirectory}' contains ',' or ';', which dotnet publish -o cannot pass to MSBuild. Use --output with another folder (default: {DefaultOutputDirectory}).");
        }

        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        var project = Path.Combine(repoRoot, "src", "Swankers.Coach", "Swankers.Coach.csproj");
        var start = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", project, "-c", "Release", "-o", outputDirectory, "-nologo" },
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish failed with exit code {process.ExitCode}.");
        }

        Verify(outputDirectory);
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
}
