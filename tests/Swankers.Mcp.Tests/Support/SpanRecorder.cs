using System.Collections.Concurrent;
using System.Diagnostics;

namespace Swankers.Mcp.Tests.Support;

/// <summary>
/// Captures Swankers.Mcp spans emitted by the in-process server (same process as the tests),
/// so tests can assert the "intent vs action" attributes.
/// </summary>
public sealed class SpanRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _activities = new();

    public SpanRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == McpDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> ToolCalls(string tool)
        => [.. _activities.Where(a => Tag(a, McpDiagnostics.ToolNameTag) == tool)];

    public static string? Tag(Activity activity, string key)
        => activity.GetTagItem(key)?.ToString();

    public void Dispose() => _listener.Dispose();
}
