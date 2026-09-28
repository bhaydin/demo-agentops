namespace Swankers.League.Tests;

public sealed class RepoPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swankers-tests", Path.GetRandomFileName());

    public RepoPathsTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "SwankersCoach.slnx"), "<Solution />");
        Directory.CreateDirectory(Path.Combine(_root, "knowledge"));
        // A source folder that would shadow "knowledge" on a case-insensitive file system.
        Directory.CreateDirectory(Path.Combine(_root, "src", "Coach", "Knowledge"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "Coach", "bin"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Absolute_paths_pass_through()
    {
        var absolute = Path.Combine(_root, "elsewhere");

        Assert.Equal(absolute, RepoPaths.Resolve(absolute, Path.Combine(_root, "src", "Coach")));
    }

    [Fact]
    public void Relative_paths_resolve_from_the_repo_root_found_above_the_content_root()
    {
        var contentRoot = Path.Combine(_root, "src", "Coach", "bin");

        var resolved = RepoPaths.Resolve("knowledge", contentRoot);

        Assert.Equal(Path.Combine(_root, "knowledge"), resolved);
    }

    [Fact]
    public void Without_a_repo_marker_relative_paths_resolve_from_the_current_directory()
    {
        var outside = Path.Combine(Path.GetTempPath(), "swankers-tests", "no-marker-" + Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        try
        {
            var resolved = RepoPaths.Resolve("data/snapshot", outside);

            // The test process itself runs inside the real repo, so the fallback finds that root.
            Assert.True(Path.IsPathRooted(resolved));
            Assert.EndsWith(Path.Combine("data", "snapshot"), resolved);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
