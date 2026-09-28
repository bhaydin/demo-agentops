namespace Swankers.League;

/// <summary>
/// Resolves repo-relative data directories (snapshots, sim state, scenarios, knowledge).
/// Absolute paths are used as configured. A relative path is taken from the repo root when
/// one can be found by walking up from the content root to SwankersCoach.slnx (the repo is
/// the unit of truth for data/ and knowledge/); otherwise from the current directory, which
/// is what a container gets. <c>dotnet run --project</c> runs from the project folder, and on
/// a case-insensitive file system a source folder can shadow a repo folder of the same name,
/// so the repo root is deliberately preferred over the current directory.
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

        var repoRoot = FindRepoRoot(contentRoot) ?? FindRepoRoot(Environment.CurrentDirectory);
        return repoRoot is null
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(repoRoot, configuredPath)); // normalizes separators
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
