using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Swankers.Evals.Evaluators;

/// <summary>
/// What a tool call returned, paired to the call by id. A call only "happened" for the rules
/// when its result exists, raised no exception, and is not an MCP error result
/// (<c>isError: true</c>, which is how a thrown McpException reaches the model).
/// </summary>
public sealed record ToolResult(int Index, string RawText, bool Succeeded, string? Error)
{
    public static ToolResult From(FunctionResultContent content, int index)
    {
        if (content.Exception is not null)
        {
            return new ToolResult(index, "", false, $"{content.Exception.GetType().Name}: {content.Exception.Message}");
        }

        var raw = content.Result switch
        {
            null => "",
            JsonElement element => element.GetRawText(),
            string text => text,
            var other => JsonSerializer.Serialize(other),
        };

        var error = ErrorIn(raw);
        return new ToolResult(index, raw, error is null, error);
    }

    /// <summary>
    /// The tool's own output. An MCP tool result reaches the model as an envelope
    /// (<c>{"content":[{"type":"text","text":"…"}]}</c>) whose text is the tool's JSON serialized
    /// again as a string, so every quote inside it is escaped; rules that look for JSON read this
    /// unwrapped text, not <see cref="RawText"/>.
    /// </summary>
    public string Text => EnvelopeText(RawText) ?? RawText;

    /// <summary>The tool's output parsed as JSON, or null when it is not JSON.</summary>
    public JsonElement? Payload
    {
        get
        {
            try
            {
                using var document = JsonDocument.Parse(Text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>The result names the player (id or name fragment), in the tool's output or the raw result.</summary>
    public bool Mentions(string idOrName)
        => Text.Contains(idOrName, StringComparison.OrdinalIgnoreCase) || RawText.Contains(idOrName, StringComparison.OrdinalIgnoreCase);

    private static string? EnvelopeText(string raw)
    {
        if (!raw.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return FirstText(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ErrorIn(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "empty result";
        }

        if (!raw.TrimStart().StartsWith('{'))
        {
            return raw.StartsWith("error", StringComparison.OrdinalIgnoreCase) ? raw : null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                return FirstText(root) ?? "tool reported isError";
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                return error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }
}

/// <summary>A tool call the agent made: arguments normalized to JSON, its position, and its result.</summary>
public sealed record ToolCall(string CallId, string Name, JsonElement Arguments, int Index, ToolResult? Result)
{
    public bool Succeeded => Result is { Succeeded: true };

    /// <summary>Short status for reports: ok, failed (reason), or no result.</summary>
    public string Status => Result switch
    {
        null => "no result",
        { Succeeded: true } => "ok",
        var r => $"failed: {r.Error}",
    };

    /// <summary>String value of an argument, or null. Non-string JSON values are returned as raw text.</summary>
    public string? Arg(string name)
    {
        if (Arguments.ValueKind != JsonValueKind.Object || !Arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText(),
        };
    }

    /// <summary>Elements of an array argument as strings (empty when absent or not an array).</summary>
    public IReadOnlyList<string> ArgStrings(string name)
    {
        if (Arguments.ValueKind != JsonValueKind.Object
            || !Arguments.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
            .ToList();
    }

    /// <summary>Every function call in the conversation, in order, paired with its result by call id.</summary>
    public static IReadOnlyList<ToolCall> From(EvalItem item) => From(item.Conversation);

    public static IReadOnlyList<ToolCall> From(IReadOnlyList<ChatMessage> conversation)
    {
        var results = new Dictionary<string, ToolResult>(StringComparer.Ordinal);
        for (var i = 0; i < conversation.Count; i++)
        {
            foreach (var result in conversation[i].Contents.OfType<FunctionResultContent>())
            {
                results[result.CallId] = ToolResult.From(result, i);
            }
        }

        var calls = new List<ToolCall>();
        for (var i = 0; i < conversation.Count; i++)
        {
            foreach (var call in conversation[i].Contents.OfType<FunctionCallContent>())
            {
                calls.Add(new ToolCall(call.CallId, call.Name, Normalize(call.Arguments), i, results.GetValueOrDefault(call.CallId)));
            }
        }

        return calls;
    }

    /// <summary>Index of the final assistant text (the recommendation); -1 when there is none.</summary>
    public static int FinalAnswerIndex(IReadOnlyList<ChatMessage> conversation)
    {
        for (var i = conversation.Count - 1; i >= 0; i--)
        {
            if (conversation[i].Role == ChatRole.Assistant && conversation[i].Contents.OfType<TextContent>().Any(t => !string.IsNullOrWhiteSpace(t.Text)))
            {
                return i;
            }
        }

        return -1;
    }

    private static JsonElement Normalize(IDictionary<string, object?>? arguments)
    {
        if (arguments is null)
        {
            return JsonDocument.Parse("{}").RootElement;
        }

        // Values arrive as JsonElement from the model or as CLR objects from tests; serializing
        // both through System.Text.Json gives one shape to query.
        return JsonSerializer.SerializeToElement(arguments);
    }
}
