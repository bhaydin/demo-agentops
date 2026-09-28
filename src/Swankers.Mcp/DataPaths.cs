namespace Swankers.Mcp;

/// <summary>
/// Resolves the data directories (snapshots, sim state, scenarios). Absolute paths are used as
/// configured. A relative path is taken from the current directory when it exists there;
/// otherwise from the repo root, found by walking up from the content root to
/// SwankersCoach.slnx. This keeps <c>dotnet run --project</c> (which runs from the project
/// folder) and Visual Studio working without per-machine configuration.
/// </summary>
public static class DataPaths
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
