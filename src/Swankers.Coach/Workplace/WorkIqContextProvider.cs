using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Swankers.Coach.Workplace;

/// <summary>A tool the Work IQ server listed, and whether the allow-list lets the provider call it.</summary>
public sealed record WorkIqTool(string Name, string? Description, JsonElement InputSchema, bool Allowed);

/// <summary>
/// The Live side of the workplace context boundary: starts the pinned Work IQ CLI as a local MCP
/// server over stdio and reads the tagged demo corpus through it. Work IQ signs the user in
/// (delegated, interactive); this class holds no credential. Work IQ's tools are never handed to
/// the model: the provider calls the one allow-listed read tool itself and returns evidence items.
/// </summary>
public sealed class WorkIqContextProvider(
    WorkplaceOptions options,
    ILoggerFactory loggerFactory,
    IClientTransport? transport = null) : IWorkplaceContextProvider
{
    private McpClient? _client;
    private IReadOnlyList<WorkIqTool> _tools = [];

    public string Mode => WorkplaceModes.Live;

    public string? Fixture => null;

    /// <summary>
    /// Receives the child process's stderr lines. They are not logged: what the CLI writes there
    /// has not been reviewed, so only a caller that keeps them private should set this.
    /// </summary>
    public Action<string>? StandardErrorSink { get; init; }

    /// <summary>Receives each raw read result, for a private capture. Never set in the demo path.</summary>
    public Action<string>? RawResultSink { get; init; }

    /// <summary>Connects and lists every tool the server offers, each marked allowed or not.</summary>
    public async Task<IReadOnlyList<WorkIqTool>> ConnectAsync(CancellationToken cancellationToken)
    {
        var workIq = options.WorkIq;

        // An injected transport lets tests stand in an in-memory MCP server. The stdio transport
        // gets no logger, so nothing the child process writes reaches the logs.
        var clientTransport = transport ?? new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "work-iq",
                Command = workIq.Command,
                Arguments = [.. workIq.EffectiveArguments],
                StandardErrorLines = StandardErrorSink ?? (_ => { }),
            },
            NullLoggerFactory.Instance);

        _client = await McpClient.CreateAsync(
            clientTransport,
            new McpClientOptions { ClientInfo = new Implementation { Name = "swankers-coach", Version = "0.4.0" } },
            loggerFactory,
            cancellationToken);

        var listed = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        _tools = [.. listed.Select(t => new WorkIqTool(t.Name, t.Description, t.JsonSchema, WorkIqToolPolicy.IsAllowed(t.Name, workIq.EffectiveAllowedTools)))];
        return _tools;
    }

    /// <summary>
    /// Calls the configured read tool and returns the evidence that carries the engagement tag,
    /// numbered E1.. in order of receipt. That order only assigns stable ids; it is not the
    /// engagement's chronology, which is each item's business date. A tool error is an error,
    /// never empty evidence.
    /// </summary>
    public async Task<IReadOnlyList<EvidenceItem>> GetEvidenceAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Connect to Work IQ before asking for evidence.");
        }

        var tool = options.WorkIq.ReadTool;
        if (!_tools.Any(t => t.Name == tool && t.Allowed))
        {
            throw new InvalidOperationException(
                $"Work IQ tool '{tool}' is not an allow-listed read tool. Listed: {string.Join(", ", _tools.Select(t => t.Name))}.");
        }

        // Metadata only: no query, no content, no reference values on the span.
        using var activity = CoachDiagnostics.ActivitySource.StartActivity($"workiq.tool {tool}", ActivityKind.Client);
        activity?.SetTag(WorkplaceTags.Scenario, CoachScenarios.Workplace);
        activity?.SetTag(WorkplaceTags.Mode, Mode);
        activity?.SetTag(WorkplaceTags.ToolName, tool);

        try
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(options.WorkIq.ReadArgumentsFor(options.EngagementTag)) ?? [];
            var result = await _client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
            var raw = WorkIqEvidenceMapper.RawText(result);
            if (result.IsError == true)
            {
                throw new InvalidOperationException($"Work IQ tool '{tool}' returned an error: {Truncate(raw)}");
            }

            RawResultSink?.Invoke(raw);
            if (WorkIqEvidenceMapper.FailedStatus(raw) is { } status)
            {
                throw new InvalidOperationException($"Work IQ tool '{tool}' returned HTTP {status} for a requested path.");
            }

            var mapped = WorkIqEvidenceMapper.Map(raw);
            var tagged = mapped
                .Where(HasEngagementTag)
                .OrderBy(i => i.ReceivedAt ?? DateTimeOffset.MaxValue)
                .Select((item, index) => item with { Id = $"E{index + 1}" })
                .ToList();

            activity?.SetTag(WorkplaceTags.Items, tagged.Count);
            activity?.SetTag(WorkplaceTags.ItemsDropped, mapped.Count - tagged.Count);
            return tagged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    private bool HasEngagementTag(EvidenceItem item)
        => item.Title?.Contains(options.EngagementTag, StringComparison.OrdinalIgnoreCase) == true
            || item.Text.Contains(options.EngagementTag, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
