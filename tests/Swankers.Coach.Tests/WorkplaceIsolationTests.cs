using Microsoft.Extensions.Configuration;
using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The workplace scenario keeps its own local response store, one per prompt version and mode,
/// and does not export to a machine-wide Application Insights. The football scenario and a
/// hosted deployment are left alone.
/// </summary>
public class WorkplaceIsolationTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "swankers-state-base");

    [Theory]
    [InlineData(null)]
    [InlineData("football")]
    [InlineData("Football")]
    [InlineData("something-else")]   // rejected later by scenario selection; isolation does not guess
    public void Nothing_changes_for_any_scenario_but_workplace(string? scenario)
    {
        var plan = WorkplaceIsolation.For(Configuration(("Coach:Scenario", scenario)), Base, hosted: false);

        Assert.Equal(WorkplaceIsolation.Plan.None, plan);
        Assert.False(plan.Applies);
        Assert.False(plan.DropApplicationInsights);
    }

    [Fact]
    public void Workplace_gets_its_own_state_root_and_drops_the_machine_wide_export()
    {
        var plan = WorkplaceIsolation.For(Configuration(("Coach:Scenario", "workplace"), ("Workplace:Mode", "Replay")), Base, hosted: false);

        Assert.True(plan.Applies);
        Assert.Equal(Path.Combine(Base, "workplace-v1-replay"), plan.StateRoot);
        Assert.True(plan.DropApplicationInsights);
    }

    [Fact]
    public void Each_prompt_version_and_mode_has_a_separate_root()
    {
        string Root(string version, string mode) => WorkplaceIsolation.For(
            Configuration(("Coach:Scenario", "Workplace"), ("Coach:PromptVersion", version), ("Workplace:Mode", mode)), Base, hosted: false).StateRoot!;

        var roots = new[] { Root("v1", "Replay"), Root("v2", "Replay"), Root("v1", "Live"), Root("v2", "Live") };

        Assert.Equal(4, roots.Distinct().Count());
        Assert.Equal(Path.Combine(Base, "workplace-v2-live"), roots[3]);
    }

    [Fact]
    public void The_default_base_is_outside_the_shared_store_and_the_repository()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentserver");
        var plan = WorkplaceIsolation.For(Configuration(("Coach:Scenario", "workplace"), ("Workplace:Mode", "Replay")), WorkplaceIsolation.DefaultStateBase, hosted: false);

        // Beside the Coach's responses/, never the same folder, and not under the checkout.
        Assert.Equal(Path.Combine(shared, "scenarios", "workplace-v1-replay"), plan.StateRoot);
        Assert.NotEqual(shared, plan.StateRoot);
        Assert.DoesNotContain(Path.Combine("artifacts", "workplace"), plan.StateRoot);
    }

    [Fact]
    public void The_base_can_be_moved_by_configuration()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "somewhere-else");
        var plan = WorkplaceIsolation.For(
            Configuration(("Coach:Scenario", "workplace"), ("Workplace:Mode", "Live"), (WorkplaceIsolation.StateBaseKey, elsewhere)), Base, hosted: false);

        Assert.Equal(Path.Combine(elsewhere, "workplace-v1-live"), plan.StateRoot);
    }

    [Theory]
    [InlineData("../v9", "workplace-.._v9-replay")]
    [InlineData("V 2", "workplace-v_2-replay")]
    [InlineData("", "workplace-unset-replay")]
    public void The_folder_name_is_made_safe(string version, string expected)
    {
        var plan = WorkplaceIsolation.For(
            Configuration(("Coach:Scenario", "workplace"), ("Coach:PromptVersion", version), ("Workplace:Mode", "Replay")), Base, hosted: false);

        Assert.Equal(Path.Combine(Base, expected), plan.StateRoot);
    }

    [Fact]
    public void A_hosted_deployment_keeps_the_platform_settings()
        => Assert.Equal(
            WorkplaceIsolation.Plan.None,
            WorkplaceIsolation.For(Configuration(("Coach:Scenario", "workplace"), ("Workplace:Mode", "Live")), Base, hosted: true));

    [Fact]
    public void Applying_for_the_football_scenario_leaves_the_process_environment_alone()
    {
        var stateRoot = Environment.GetEnvironmentVariable(WorkplaceIsolation.StateRootVariable);
        var appInsights = Environment.GetEnvironmentVariable(WorkplaceIsolation.ApplicationInsightsVariable);

        var plan = WorkplaceIsolation.Apply(["--Coach:Scenario", "football"]);

        Assert.False(plan.Applies);
        Assert.Equal(stateRoot, Environment.GetEnvironmentVariable(WorkplaceIsolation.StateRootVariable));
        Assert.Equal(appInsights, Environment.GetEnvironmentVariable(WorkplaceIsolation.ApplicationInsightsVariable));
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build();
}
