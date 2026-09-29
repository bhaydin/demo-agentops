using Microsoft.Extensions.AI;
using Swankers.Evals.Evaluators;

namespace Swankers.Evals;

/// <summary>
/// The rules must read what the MCP client actually hands the model, not a hand-written shape.
/// CI run 36514287848 showed set_lineup results arriving as a serialized AIContent
/// (<c>{"$type":"text","Text":"…"}</c>) that the lineup rule could not read while unit tests
/// built on an assumed envelope passed. These tests call the real tools in-process (no model).
/// </summary>
public sealed class ToolResultShapeTests(ITestOutputHelper output)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static readonly string[] Starters = ["16580", "14071", "17051", "15281", "14102", "16641", "13719", "0510"];

    [Fact]
    public async Task Lineup_and_news_results_from_the_real_client_are_readable()
    {
        await using var league = await McpServerUnderTest.StartAsync(CT);
        await league.ResetAsync(scenario: null, CT);
        var setLineup = league.Tools.OfType<AIFunction>().Single(t => t.Name == "set_lineup");
        var news = league.Tools.OfType<AIFunction>().Single(t => t.Name == "get_player_news");

        var set = await InvokeAsync(setLineup, new AIFunctionArguments { ["starters"] = Starters });
        var rejected = await InvokeAsync(setLineup, new AIFunctionArguments { ["starters"] = new[] { "17051", "9999" } });
        var lookedUp = await InvokeAsync(news, new AIFunctionArguments { ["player"] = "17051" });
        output.WriteLine($"set_lineup result: {set.Result?.GetType().FullName ?? "null"} exception={set.Exception?.GetType().Name}");
        output.WriteLine($"rejected result: {rejected.Result?.GetType().FullName ?? "null"} exception={rejected.Exception?.GetType().Name} raw={ToolResult.From(rejected, 0).RawText}");

        var golden = new GoldenCase { Id = "ss-x", Category = GoldenCase.Categories.StartSit, Query = "q", MustStart = ["17051", "14071"], MustNotStart = ["14823"] };
        var lineup = ToolSequenceEvaluator.LineupPlayers(golden, Calls(("set_lineup", set)));
        var rejectedLineup = ToolSequenceEvaluator.LineupPlayers(golden, Calls(("set_lineup", rejected)));
        var newsResult = ToolResult.From(lookedUp, 1);

        Assert.True(lineup.Passed, lineup.Reason);
        Assert.False(rejectedLineup.Passed);
        Assert.Contains("never succeeded", rejectedLineup.Reason);
        Assert.Contains("9999", rejectedLineup.Reason);
        Assert.True(newsResult.Succeeded, newsResult.Error);
        Assert.True(newsResult.Mentions("17051"));
        Assert.True(newsResult.Mentions("Judkins"));
    }

    /// <summary>Invokes the tool the way the function-invoking chat client does: a thrown exception becomes the result's Exception.</summary>
    private static async Task<FunctionResultContent> InvokeAsync(AIFunction function, AIFunctionArguments arguments)
    {
        var callId = Guid.NewGuid().ToString("N")[..12];
        try
        {
            return new FunctionResultContent(callId, await function.InvokeAsync(arguments, CT));
        }
        catch (Exception ex)
        {
            return new FunctionResultContent(callId, "Error: Function failed.") { Exception = ex };
        }
    }

    private static IReadOnlyList<ToolCall> Calls(params (string Tool, FunctionResultContent Result)[] results)
    {
        var conversation = new List<ChatMessage>();
        foreach (var (tool, result) in results)
        {
            conversation.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(result.CallId, tool, new Dictionary<string, object?>())]));
            conversation.Add(new ChatMessage(ChatRole.Tool, [result]));
        }

        return ToolCall.From(conversation);
    }
}
