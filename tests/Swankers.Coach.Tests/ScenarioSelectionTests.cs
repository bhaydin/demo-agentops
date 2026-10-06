using Swankers.Coach.Prompts;

namespace Swankers.Coach.Tests;

/// <summary>
/// One process hosts one scenario, chosen at startup. Football stays the default, so the hosted
/// Coach versions and the Summit demos behave as before; anything unrecognized stops startup.
/// </summary>
public class ScenarioSelectionTests
{
    [Fact]
    public void Football_is_the_default()
    {
        Assert.Equal(CoachScenarios.Football, new CoachOptions().Scenario);
        Assert.Equal(CoachScenarios.Football, CoachScenarios.Parse(new CoachOptions().Scenario));
    }

    [Theory]
    [InlineData("football", CoachScenarios.Football)]
    [InlineData("workplace", CoachScenarios.Workplace)]
    [InlineData("Workplace", CoachScenarios.Workplace)]
    [InlineData(" FOOTBALL ", CoachScenarios.Football)]
    public void Known_scenarios_parse_to_their_canonical_name(string configured, string expected)
        => Assert.Equal(expected, CoachScenarios.Parse(configured));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("meeting-prep")]
    [InlineData("footbal")]
    public void An_unknown_scenario_is_rejected_and_the_valid_ones_are_named(string? configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CoachScenarios.Parse(configured));

        Assert.Contains("Coach:Scenario", ex.Message);
        Assert.Contains("football, workplace", ex.Message);
    }

    [Fact]
    public void Each_scenario_lists_only_its_own_prompt_versions()
    {
        var coach = new PromptLibrary(PromptLibrary.DefaultDirectory).Versions;
        var meetingPrep = new PromptLibrary(PromptLibrary.DefaultDirectory, PromptLibrary.MeetingPrepPrefix).Versions;

        Assert.Equal(["v0", "v1", "v2"], coach);
        Assert.Equal(["v1", "v1.1", "v2"], meetingPrep);
    }

    // --- the prompt version each scenario runs by default --------------------------------------

    [Fact]
    public void The_prompt_version_is_unset_until_the_scenario_resolves_it()
        => Assert.Null(new CoachOptions().PromptVersion);

    [Theory]
    [InlineData(CoachScenarios.Football, "v1")]      // the hardened Coach the Summit demos and hosted versions rely on
    [InlineData(CoachScenarios.Workplace, "v1.1")]   // the accepted WorkplaceMeetingPrep baseline, not v1
    [InlineData("Workplace", "v1.1")]
    public void Each_scenario_has_its_own_default_prompt_version(string scenario, string expected)
        => Assert.Equal(expected, CoachScenarios.DefaultPromptVersion(scenario));

    [Fact]
    public void Each_default_exists_in_its_own_prompt_library()
    {
        var coach = new PromptLibrary(PromptLibrary.DefaultDirectory).Load(CoachScenarios.DefaultPromptVersion(CoachScenarios.Football));
        var meetingPrep = new PromptLibrary(PromptLibrary.DefaultDirectory, PromptLibrary.MeetingPrepPrefix).Load(CoachScenarios.DefaultPromptVersion(CoachScenarios.Workplace));

        Assert.Contains("get_player_news", coach);
        Assert.StartsWith("# Meeting prep instructions (v1.1)", meetingPrep);
    }

    [Theory]
    [InlineData(CoachScenarios.Football, null, "v1")]
    [InlineData(CoachScenarios.Football, "", "v1")]
    [InlineData(CoachScenarios.Football, "v2", "v2")]
    [InlineData(CoachScenarios.Workplace, null, "v1.1")]
    [InlineData(CoachScenarios.Workplace, "   ", "v1.1")]
    [InlineData(CoachScenarios.Workplace, "v1", "v1")]      // an explicit version always wins, so a comparison never runs on the default
    [InlineData(CoachScenarios.Workplace, " v2 ", "v2")]
    public void An_explicit_version_wins_and_an_unset_one_falls_back_to_the_scenario_default(string scenario, string? configured, string expected)
        => Assert.Equal(expected, CoachScenarios.PromptVersionFor(scenario, configured));

    [Fact]
    public void An_unknown_scenario_has_no_default_prompt_version()
        => Assert.Throws<InvalidOperationException>(() => CoachScenarios.DefaultPromptVersion("meetings"));

    [Theory]
    [InlineData(PromptLibrary.CoachPrefix, "v1.1", "coach-v1.1.md", "Available: v0, v1, v2.")]           // a workplace version on the football Coach
    [InlineData(PromptLibrary.MeetingPrepPrefix, "v0", "meetingprep-v0.md", "Available: v1, v1.1, v2.")]  // a Coach version on the workplace scenario
    public void A_version_the_scenario_does_not_have_fails_and_names_the_file_and_the_versions_that_exist(string prefix, string version, string file, string available)
    {
        var ex = Assert.Throws<FileNotFoundException>(() => new PromptLibrary(PromptLibrary.DefaultDirectory, prefix).Load(version));

        Assert.Contains($"No instructions for prompt version '{version}' ({file}).", ex.Message);
        Assert.Contains(available, ex.Message);
    }

    [Fact]
    public void The_agent_keeps_the_coach_description_unless_a_scenario_gives_its_own()
    {
        var options = new CoachOptions { ProjectEndpoint = "https://example.invalid/api/projects/none", AgentName = "Coach" };

        var coach = CoachAgentFactory.Create(options, "instructions", [], Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var other = CoachAgentFactory.Create(options, "instructions", [], Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, "Another scenario.");

        Assert.Equal(CoachAgentFactory.Description, coach.Description);
        Assert.Equal("Coach", coach.Name);
        Assert.Equal("Another scenario.", other.Description);
    }
}
