using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Swankers.Coach.Prompts;
using Swankers.League;

namespace Swankers.Coach.Workplace;

/// <summary>What Program.cs needs to host the workplace scenario, plus the provenance a run should log.</summary>
public sealed record WorkplaceComposition(
    string AgentName,
    string Instructions,
    string PromptId,
    string PromptSha256,
    IReadOnlyList<AITool> Tools,
    IWorkplaceContextProvider Provider,
    DateTimeOffset ReferenceTime)
{
    public string Mode => Provider.Mode;
}

/// <summary>
/// Composes the WorkplaceMeetingPrep scenario: its own versioned instructions, its one read-only
/// tool, and a Live or Replay evidence provider. It needs no MCP credential, no league MCP server,
/// no league knowledge, and no SimLeague, and it exposes no football tool.
/// </summary>
public static class WorkplaceScenario
{
    public const string Description = "Meeting-preparation assistant for one client engagement, grounded in retrieved workplace evidence.";

    public static async Task<WorkplaceComposition> ComposeAsync(
        WorkplaceOptions options,
        string promptVersion,
        string promptsDirectory,
        string contentRoot,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var mode = WorkplaceModes.Parse(options.Mode);
        var referenceTime = ParseReferenceTime(options.ReferenceTime);

        // Line endings are normalized so the model reads, and the hash covers, the same bytes on
        // every checkout. (The Coach's prompts are loaded as they are on disk; that is unchanged.)
        var instructions = new PromptLibrary(promptsDirectory, PromptLibrary.MeetingPrepPrefix).Load(promptVersion).ReplaceLineEndings("\n");
        var promptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instructions)));

        IWorkplaceContextProvider provider = mode == WorkplaceModes.Live
            ? await ConnectLiveAsync(options, loggerFactory, cancellationToken)
            : FixtureContextProvider.Load(RepoPaths.Resolve(options.FixturePath, contentRoot));

        return new WorkplaceComposition(
            AgentName: AgentNameFor(options.AgentName, promptVersion),
            Instructions: instructions,
            PromptId: $"{PromptLibrary.MeetingPrepPrefix}{promptVersion}",
            PromptSha256: promptSha256,
            Tools: [new EngagementEvidenceTool(provider, options, referenceTime).AsTool()],
            Provider: provider,
            ReferenceTime: referenceTime);
    }

    /// <summary>
    /// The agent's name for a prompt version: distinct per version, so sessions and traces of two
    /// versions cannot be confused. A dotted version such as v1.1 becomes v1-1, because an agent
    /// name is letters, digits, underscores, and hyphens.
    /// </summary>
    public static string AgentNameFor(string baseName, string promptVersion) => $"{baseName}-{promptVersion.Replace('.', '-')}";

    /// <summary>An explicit offset is required: a time without one would be read in the machine's zone.</summary>
    public static DateTimeOffset ParseReferenceTime(string configured)
        => DateTimeOffset.TryParseExact(configured, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Workplace:ReferenceTime '{configured}' must be ISO 8601 with an offset, for example 2026-10-07T09:00:00-05:00.");

    // Live connects up front, like the Coach's league MCP: a failure stops startup. It is never
    // replaced by the fixture.
    private static async Task<IWorkplaceContextProvider> ConnectLiveAsync(WorkplaceOptions options, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var live = new WorkIqContextProvider(options, loggerFactory);
        try
        {
            await live.ConnectAsync(cancellationToken);
            return live;
        }
        catch
        {
            await live.DisposeAsync();
            throw;
        }
    }
}
