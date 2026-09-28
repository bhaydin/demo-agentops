namespace Swankers.League;

/// <summary>
/// Resolves repo-relative data directories (snapshots, sim state, scenarios, knowledge).
/// Absolute paths are used as configured. A relative path is taken from the current directory
/// when it exists there; otherwise from the repo root, found by walking up from the content
/// root to SwankersCoach.slnx. <c>dotnet run --project</c> runs from the project folder (and
/// ignores a launch profile's workingDirectory), so this keeps local runs working without
/// per-machine configuration.
/// </summary>
public static class RepoPaths
{
    private const string RepoMarker = "SwankersCoach.slnx";

    public static string Resolve(string configuredPath, string contentRoot)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var fromCurrent = Path.GetFullPath(configuredPath);
        if (Directory.Exists(fromCurrent))
        {
            return fromCurrent;
        }

        var repoRoot = FindRepoRoot(contentRoot);
        return repoRoot is null ? fromCurrent : Path.Combine(repoRoot, configuredPath);
    }

    public static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoMarker)))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
