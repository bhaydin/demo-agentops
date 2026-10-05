using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Swankers.Coach.Workplace;

/// <summary>
/// Turns a Work IQ tool result into evidence items. The fetch shape was confirmed against Work IQ
/// CLI 1.0.1.47154 on 2026-10-05 for a mail collection (demo/winet-workiq-evidence.md):
/// <c>{"results":[{"data":{"value":[message, ...]},"statusCode":200}],"note":"..."}</c>, each
/// message carrying the selected Graph fields (id, subject, receivedDateTime, from, bodyPreview,
/// webLink). The ask shape, <c>{"response": "...", "conversationId": "..."}</c>, is from the
/// documentation only (Learn, "Work IQ MCP tool reference") and has not been exercised.
/// Ids and links are copied as returned; nothing is invented when a field is absent.
/// </summary>
public static class WorkIqEvidenceMapper
{
    public const string EmailSourceType = "email";
    public const string SynthesizedAnswerSourceType = "synthesized_answer";

    /// <summary>The result as the server sent it: structured content when present, else its text.</summary>
    public static string RawText(CallToolResult result)
        => result.StructuredContent is { } structured
            ? structured.GetRawText()
            : string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public static IReadOnlyList<EvidenceItem> Map(CallToolResult result) => Map(RawText(result));

    /// <summary>
    /// The first per-path HTTP status outside 2xx, or null. A fetch reports each path's status
    /// beside its data, so a refused path arrives as a successful tool call; without this check
    /// it would read as "no evidence".
    /// </summary>
    public static int? FailedStatus(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty("statusCode", out var status)
                    && status.TryGetInt32(out var code)
                    && code is < 200 or > 299)
                {
                    return code;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<EvidenceItem> Map(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(raw);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Plain text is an answer Work IQ wrote, not something it retrieved.
            return [Answer(raw)];
        }

        var items = new List<EvidenceItem>();
        CollectMessages(root, items);
        if (items.Count == 0 && root.ValueKind == JsonValueKind.Object && Text(root, "response") is { Length: > 0 } response)
        {
            items.Add(Answer(response));
        }

        return items;
    }

    private static void CollectMessages(JsonElement element, List<EvidenceItem> items)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when IsMessage(element):
                items.Add(Message(element));
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(property.Value, items);
                }

                break;
            case JsonValueKind.Array:
                foreach (var entry in element.EnumerateArray())
                {
                    CollectMessages(entry, items);
                }

                break;
        }
    }

    /// <summary>A Graph message: an id plus a subject or a body preview.</summary>
    private static bool IsMessage(JsonElement element)
        => Text(element, "id") is { Length: > 0 } && (Text(element, "subject") is not null || Text(element, "bodyPreview") is not null);

    private static EvidenceItem Message(JsonElement message)
    {
        var address = Object(Object(message, "from"), "emailAddress");
        var body = Text(Object(message, "body"), "content");

        return new EvidenceItem(
            Id: "",
            SourceType: EmailSourceType,
            Title: Text(message, "subject"),
            Author: Text(address, "name") ?? Text(address, "address"),
            ReceivedAt: Time(message, "receivedDateTime") ?? Time(message, "sentDateTime") ?? Time(message, "createdDateTime"),
            Text: !string.IsNullOrWhiteSpace(body) ? body : Text(message, "bodyPreview") ?? "",
            SourceRef: Text(message, "id"),
            SourceUrl: Text(message, "webLink"));
    }

    private static EvidenceItem Answer(string text)
        => new("", SynthesizedAnswerSourceType, Title: null, Author: null, ReceivedAt: null, text, SourceRef: null, SourceUrl: null, Synthesized: true);

    /// <summary>A child object, or an undefined element when it is absent.</summary>
    private static JsonElement Object(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Time(JsonElement element, string name)
        => Text(element, name) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
}
