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
        Assert.Equal(["v1", "v1.1"], meetingPrep);
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
