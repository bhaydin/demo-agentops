using Swankers.AgentDeploy;

namespace Swankers.AgentDeploy.Tests;

/// <summary>Stage labels route to the newest version of that stage; unknown labels say what exists.</summary>
public sealed class RouteLabelTests
{
    private static readonly AgentDeployer.StageVersion[] Versions =
    [
        new("4", "v1-owner"),
        new("5", "v2-owner"),
        new("6", "v1-commissioner"),
        new("7", "v1-owner"),
        new("9", "v0-commissioner"),
        new("10", null),
    ];

    [Fact]
    public void A_label_with_several_versions_resolves_to_the_newest()
        => Assert.Equal("7", AgentDeployer.ResolveLabel(Versions, "v1-owner"));

    [Fact]
    public void Labels_are_case_insensitive_and_single_matches_resolve()
    {
        Assert.Equal("5", AgentDeployer.ResolveLabel(Versions, "V2-Owner"));
        Assert.Equal("9", AgentDeployer.ResolveLabel(Versions, "v0-commissioner"));
    }

    [Fact]
    public void Versions_compare_numerically_not_as_text()
        => Assert.Equal("10", AgentDeployer.ResolveLabel([new("9", "x"), new("10", "x")], "x"));

    [Fact]
    public void An_unknown_label_lists_the_stages_that_exist()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AgentDeployer.ResolveLabel(Versions, "v3-owner"));

        Assert.Contains("No version has stage 'v3-owner'", ex.Message);
        Assert.Contains("v1-owner (v7)", ex.Message);
        Assert.Contains("v0-commissioner (v9)", ex.Message);
        Assert.DoesNotContain("v10", ex.Message);
    }
}
