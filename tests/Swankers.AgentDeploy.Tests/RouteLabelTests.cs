using Swankers.AgentDeploy;

namespace Swankers.AgentDeploy.Tests;

/// <summary>
/// Stage labels route to the newest active version of that stage; a failed or still-creating
/// version is never chosen (Codex Phase 7); unknown labels say what exists.
/// </summary>
public sealed class RouteLabelTests
{
    private static readonly AgentDeployer.StageVersion[] Versions =
    [
        new("4", "v1-owner", "Active"),
        new("5", "v2-owner", "Active"),
        new("6", "v1-commissioner", "Active"),
        new("7", "v1-owner", "Active"),
        new("9", "v0-commissioner", "Active"),
        new("10", null, "Active"),
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
        => Assert.Equal("10", AgentDeployer.ResolveLabel([new("9", "x", "Active"), new("10", "x", "Active")], "x"));

    [Theory]
    [InlineData("Failed")]
    [InlineData("Creating")]
    [InlineData(null)]
    public void A_newer_version_that_is_not_active_is_skipped(string? status)
    {
        var versions = Versions.Append(new AgentDeployer.StageVersion("11", "v1-owner", status));

        Assert.Equal("7", AgentDeployer.ResolveLabel(versions, "v1-owner"));
    }

    [Fact]
    public void A_label_whose_only_versions_are_not_active_fails_and_names_their_states()
    {
        var versions = new AgentDeployer.StageVersion[] { new("12", "v3-owner", "Failed"), new("13", "v3-owner", "Creating") };

        var ex = Assert.Throws<InvalidOperationException>(() => AgentDeployer.ResolveLabel(versions, "v3-owner"));

        Assert.Contains("No active version has stage 'v3-owner'", ex.Message);
        Assert.Contains("v12 Failed", ex.Message);
        Assert.Contains("v13 Creating", ex.Message);
    }

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
