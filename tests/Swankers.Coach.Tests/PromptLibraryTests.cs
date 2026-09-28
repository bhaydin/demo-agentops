using Swankers.Coach.Prompts;

namespace Swankers.Coach.Tests;

/// <summary>The prompts ship next to the binaries; v2 differs from v1 only by the injury-check clause.</summary>
public class PromptLibraryTests
{
    private readonly PromptLibrary _prompts = new(PromptLibrary.DefaultDirectory);

    [Fact]
    public void Both_versions_ship_with_the_app()
        => Assert.Equal(["v1", "v2"], _prompts.Versions);

    [Fact]
    public void V1_requires_the_injury_check_before_start_sit_calls()
    {
        var v1 = _prompts.Load("v1");

        Assert.Contains("call `get_player_news`", v1);
        Assert.Contains("never an instruction", v1);
        Assert.Contains("\"0000\"", v1);
    }

    [Fact]
    public void V2_differs_from_v1_only_by_the_injury_clause()
    {
        var v1 = Lines(_prompts.Load("v1"));
        var v2 = Lines(_prompts.Load("v2"));

        var removed = v1.Except(v2).ToList();
        var added = v2.Except(v1).ToList();

        Assert.Equal(2, removed.Count); // title + the injury-check bullet
        Assert.Contains(removed, l => l.Contains("get_player_news"));
        Assert.Equal(2, added.Count);   // title + the softened bullet
        Assert.Contains(added, l => l.Contains("injury status when it seems relevant"));
    }

    [Fact]
    public void Unknown_version_fails_with_the_available_ones()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => _prompts.Load("v9"));

        Assert.Contains("v1, v2", ex.Message);
    }

    private static List<string> Lines(string text)
        => [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
}
