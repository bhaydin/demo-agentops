namespace Swankers.Coach.Workplace;

/// <summary>
/// Binds the "Workplace" configuration section: the WorkplaceMeetingPrep scenario's settings,
/// separate from the football <see cref="CoachOptions"/>. Nothing here is a secret: Work IQ signs
/// the user in itself, and this process never holds a Microsoft 365 token.
/// </summary>
public sealed class WorkplaceOptions
{
    public const string SectionName = "Workplace";

    /// <summary>
    /// Live (Work IQ) or Replay (the versioned fixture). Required; see <see cref="WorkplaceModes"/>.
    /// There is no default, and one never stands in for the other.
    /// </summary>
    public string Mode { get; set; } = "";

    /// <summary>
    /// The scenario's "now", ISO 8601 with an explicit offset. "Latest" and "before my meeting"
    /// are relative to this, never to the system clock.
    /// </summary>
    public string ReferenceTime { get; set; } = "2026-10-07T09:00:00-05:00";

    /// <summary>
    /// Tag every corpus item carries. Evidence that does not mention it is dropped, so a query
    /// that is too broad cannot put unrelated mail in front of the model.
    /// </summary>
    public string EngagementTag { get; set; } = "NW-DPM";

    /// <summary>The Replay evidence fixture, relative to the repo root unless rooted.</summary>
    public string FixturePath { get; set; } = Path.Combine("data", "workplace", "northwind-v1.evidence.json");

    /// <summary>The scenario's agent name; the prompt version is appended so sessions and traces of different versions cannot be confused.</summary>
    public string AgentName { get; set; } = "MeetingPrep";

    public WorkIqOptions WorkIq { get; set; } = new();
}

/// <summary>
/// How the provider starts and calls the Work IQ CLI's local MCP server. The defaults are the
/// configuration verified against Work IQ CLI 1.0.1.47154 on 2026-10-05 (demo/winet-workiq-evidence.md).
/// </summary>
public sealed class WorkIqOptions
{
    /// <summary>The one Work IQ tool this scenario calls: a read of entities by path.</summary>
    public const string FetchTool = "fetch";

    /// <summary>
    /// The Work IQ CLI, installed globally at the pinned version
    /// (<c>npm install -g @microsoft/workiq@1.0.1</c>). Never <c>npx</c> or <c>@latest</c>.
    /// </summary>
    public string Command { get; set; } = "workiq";

    /// <summary>Arguments that start the stdio MCP server; empty means <c>mcp</c>.</summary>
    public IList<string> Arguments { get; set; } = [];

    /// <summary>
    /// The signed-in account to use, passed as <c>--account</c>; empty uses the CLI's default
    /// account. Set it per machine; it does not belong in the repository.
    /// </summary>
    public string Account { get; set; } = "";

    /// <summary>
    /// Exact names of the Work IQ tools the provider may call; empty means <see cref="FetchTool"/>
    /// only. A tool that creates, changes, deletes, or sends is refused even when listed here.
    /// </summary>
    public IList<string> AllowedTools { get; set; } = [];

    /// <summary>The allow-listed tool that reads the corpus.</summary>
    public string ReadTool { get; set; } = FetchTool;

    /// <summary>The read tool's arguments as a JSON object; empty means <see cref="InboxQuery"/> for the engagement tag.</summary>
    public string ReadArgumentsJson { get; set; } = "";

    public IReadOnlyList<string> EffectiveArguments
    {
        get
        {
            List<string> arguments = Arguments.Count > 0 ? [.. Arguments] : ["mcp"];
            if (Account.Length > 0)
            {
                arguments.AddRange(["--account", Account]);
            }

            return arguments;
        }
    }

    public IReadOnlyList<string> EffectiveAllowedTools => AllowedTools.Count > 0 ? [.. AllowedTools] : [FetchTool];

    public string ReadArgumentsFor(string engagementTag)
        => !string.IsNullOrWhiteSpace(ReadArgumentsJson) ? ReadArgumentsJson : InboxQuery(engagementTag);

    /// <summary>
    /// The verified corpus query: Inbox messages whose subject carries the bracketed tag. Inbox
    /// only, because seed mail sent to oneself is also in Sent Items and <c>/me/messages</c>
    /// returns each item twice. <c>bodyPreview</c> is Graph's plain-text preview, at most 255 characters.
    /// </summary>
    public static string InboxQuery(string engagementTag)
        => $$"""{"entityUrls":["/me/mailFolders/inbox/messages?$filter=contains(subject,{{Uri.EscapeDataString($"'[{engagementTag}]'")}})&$select=id,subject,receivedDateTime,from,bodyPreview,webLink&$top=25"]}""";
}
