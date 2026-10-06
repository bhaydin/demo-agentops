using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The workplace scenario is read-only: exact names, fetch only unless configured otherwise, and
/// no mutation tool even when someone lists it. The tool names are the twelve the pinned Work IQ
/// server listed on 2026-10-05, plus spellings its documentation and plugin catalog use.
/// </summary>
public class WorkIqToolPolicyTests
{
    private static readonly string[] ListedByThePinnedServer =
    [
        "accept_eula", "ask", "call_function", "create_entity", "delete_entity", "do_action",
        "fetch", "fetch_blob", "get_schema", "list_agents", "search_paths", "update_entity",
    ];

    [Theory]
    [InlineData("create_entity")]
    [InlineData("update_entity")]
    [InlineData("delete_entity")]
    [InlineData("do_action")]
    [InlineData("accept_eula")]
    [InlineData("create_entity_work_iq")]
    [InlineData("do_action_work_iq")]
    [InlineData("createEntity")]
    [InlineData("send_mail")]
    public void Mutation_tools_are_never_allowed_even_when_listed(string tool)
    {
        Assert.True(WorkIqToolPolicy.IsMutation(tool));
        Assert.False(WorkIqToolPolicy.IsAllowed(tool, [tool]));
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("fetch_blob")]
    [InlineData("fetch_work_iq")]
    [InlineData("ask")]
    [InlineData("get_schema")]
    [InlineData("list_agents")]
    [InlineData("search_paths")]
    [InlineData("call_function")]
    public void Read_tools_are_allowed_only_by_exact_name(string tool)
    {
        Assert.False(WorkIqToolPolicy.IsMutation(tool));
        Assert.True(WorkIqToolPolicy.IsAllowed(tool, [tool]));
        Assert.False(WorkIqToolPolicy.IsAllowed(tool, []));
        Assert.False(WorkIqToolPolicy.IsAllowed(tool, [tool.ToUpperInvariant()]));
        Assert.False(WorkIqToolPolicy.IsAllowed(tool, [tool + "_other"]));
    }

    [Fact]
    public void By_default_only_fetch_is_allowed_out_of_everything_the_server_lists()
    {
        var workIq = new WorkplaceOptions().WorkIq;

        Assert.Equal(["fetch"], workIq.EffectiveAllowedTools);
        Assert.Equal("fetch", workIq.ReadTool);
        Assert.Equal(["fetch"], ListedByThePinnedServer.Where(t => WorkIqToolPolicy.IsAllowed(t, workIq.EffectiveAllowedTools)));
    }

    [Fact]
    public void Listing_every_tool_still_leaves_the_five_mutation_tools_refused()
        => Assert.Equal(
            ["accept_eula", "create_entity", "delete_entity", "do_action", "update_entity"],
            ListedByThePinnedServer.Where(t => !WorkIqToolPolicy.IsAllowed(t, ListedByThePinnedServer)));

    [Fact]
    public void The_default_read_is_the_verified_inbox_query_for_the_engagement_tag()
    {
        var options = new WorkplaceOptions();

        // Exactly the arguments the 2026-10-05 live run passed (demo/winet-workiq-evidence.md).
        Assert.Equal(
            """{"entityUrls":["/me/mailFolders/inbox/messages?$filter=contains(subject,%27%5BNW-DPM%5D%27)&$select=id,subject,receivedDateTime,from,bodyPreview,webLink&$top=25"]}""",
            options.WorkIq.ReadArgumentsFor(options.EngagementTag));
        Assert.DoesNotContain("/me/messages", options.WorkIq.ReadArgumentsFor(options.EngagementTag));   // Inbox and Sent Items would both match
        Assert.Equal("""{"entityUrls":["/me/events"]}""", new WorkIqOptions { ReadArgumentsJson = """{"entityUrls":["/me/events"]}""" }.ReadArgumentsFor("NW-DPM"));
    }

    [Fact]
    public void The_server_is_started_with_mcp_and_an_account_only_when_one_is_configured()
    {
        Assert.Equal(["mcp"], new WorkIqOptions().EffectiveArguments);
        Assert.Equal(["mcp", "--account", "someone@example.invalid"], new WorkIqOptions { Account = "someone@example.invalid" }.EffectiveArguments);
        Assert.Equal(["mcp", "--log-level", "Warning"], new WorkIqOptions { Arguments = ["mcp", "--log-level", "Warning"] }.EffectiveArguments);
    }
}
