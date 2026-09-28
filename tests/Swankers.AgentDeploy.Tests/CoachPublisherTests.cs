using Swankers.AgentDeploy;

namespace Swankers.AgentDeploy.Tests;

/// <summary>
/// Codex Phase 4 P1: the publisher used to delete any existing output folder, including a
/// checkout passed by mistake. Every invalid destination must leave existing files intact,
/// and only a folder the tool created (marker file) may be emptied.
/// </summary>
public sealed class CoachPublisherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swankers-agentdeploy-tests", Guid.NewGuid().ToString("N"));
    private readonly string _checkout;
    private readonly string _solution;
    private readonly string _project;

    public CoachPublisherTests()
    {
        _checkout = Path.Combine(_root, "checkout");
        _solution = Path.Combine(_checkout, "SwankersCoach.slnx");
        _project = Path.Combine(_checkout, "src", "Swankers.Coach", "Swankers.Coach.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(_project)!);
        File.WriteAllText(_solution, "<Solution />");
        File.WriteAllText(_project, "<Project />");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Refuses_the_checkout_root_and_keeps_its_files()
    {
        var ex = Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(_checkout, _checkout));

        Assert.Contains("checkout", ex.Message);
        AssertCheckoutIntact();
    }

    [Fact]
    public void Refuses_a_folder_inside_the_checkout()
    {
        var inside = Path.Combine(_checkout, "artifacts", "coach-publish");

        Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(inside, _checkout));

        AssertCheckoutIntact();
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public void Refuses_an_ancestor_of_the_checkout()
    {
        Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(_root, _checkout));

        AssertCheckoutIntact();
    }

    [Fact]
    public void Refuses_a_relative_path_that_resolves_to_the_checkout()
    {
        // "." style inputs used to bypass the raw-string checks; normalization catches them.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, _checkout);

        Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(relative, _checkout));

        AssertCheckoutIntact();
    }

    [Fact]
    public void Refuses_a_filesystem_root()
    {
        var root = Path.GetPathRoot(_root)!;

        var ex = Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(root, _checkout));

        Assert.Contains("root", ex.Message);
    }

    [Fact]
    public void Refuses_a_path_msbuild_cannot_take()
    {
        var comma = Path.Combine(_root, "Contoso, Inc", "publish");

        var ex = Assert.Throws<ArgumentException>(() => CoachPublisher.PrepareOutputDirectory(comma, _checkout));

        Assert.Contains("','", ex.Message);
        Assert.False(Directory.Exists(comma));
    }

    [Fact]
    public void Leaves_a_folder_it_does_not_own_untouched()
    {
        var foreign = Path.Combine(_root, "someone-elses-folder");
        Directory.CreateDirectory(foreign);
        var notes = Path.Combine(foreign, "notes.txt");
        File.WriteAllText(notes, "keep me");

        var ex = Assert.Throws<InvalidOperationException>(() => CoachPublisher.PrepareOutputDirectory(foreign, _checkout));

        Assert.Contains("nothing was deleted", ex.Message);
        Assert.Equal("keep me", File.ReadAllText(notes));
        Assert.False(File.Exists(Path.Combine(foreign, CoachPublisher.OwnershipMarker)));
    }

    [Fact]
    public void Creates_a_missing_folder_and_marks_it()
    {
        var output = Path.Combine(_root, "new", "publish");

        var prepared = CoachPublisher.PrepareOutputDirectory(output, _checkout);

        Assert.Equal(Path.GetFullPath(output), prepared);
        Assert.True(File.Exists(Path.Combine(prepared, CoachPublisher.OwnershipMarker)));
        Assert.Single(Directory.EnumerateFileSystemEntries(prepared));
    }

    [Fact]
    public void Accepts_an_empty_folder()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var prepared = CoachPublisher.PrepareOutputDirectory(empty, _checkout);

        Assert.True(File.Exists(Path.Combine(prepared, CoachPublisher.OwnershipMarker)));
    }

    [Fact]
    public void Replaces_only_a_folder_it_owns()
    {
        var owned = Path.Combine(_root, "owned");
        CoachPublisher.PrepareOutputDirectory(owned, _checkout);
        var stale = Path.Combine(owned, "prompts", "stale.md");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old bundle");

        var prepared = CoachPublisher.PrepareOutputDirectory(owned + Path.DirectorySeparatorChar, _checkout);

        Assert.Equal(Path.GetFullPath(owned), prepared);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(prepared, CoachPublisher.OwnershipMarker)));
    }

    private void AssertCheckoutIntact()
    {
        Assert.True(File.Exists(_solution), "solution marker was deleted");
        Assert.True(File.Exists(_project), "project file was deleted");
    }
}
