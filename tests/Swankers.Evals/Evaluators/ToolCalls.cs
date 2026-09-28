using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Swankers.Evals.Evaluators;

/// <summary>A tool call the agent made, with its arguments normalized to JSON.</summary>
public sealed record ToolCall(string Name, JsonElement Arguments)
{
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

    /// <summary>Every function call in the conversation, in order.</summary>
    public static IReadOnlyList<ToolCall> From(EvalItem item) => From(item.Conversation);

    public static IReadOnlyList<ToolCall> From(IEnumerable<ChatMessage> conversation)
        => conversation
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(call => new ToolCall(call.Name, Normalize(call.Arguments)))
            .ToList();

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
