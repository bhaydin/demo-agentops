using System.Globalization;
using System.Text.Json;

namespace Swankers.League.Mfl;

/// <summary>
/// Helpers for MFL's JSON quirks (verified against live responses, 2026-09-27):
/// every value is a string, empty collections are <c>{}</c>, and a collection with a single
/// item may be an object instead of a one-element array.
/// </summary>
internal static class MflJson
{
    /// <summary>
    /// Enumerates <c>parent.propertyName</c> as a collection, tolerating a missing property,
    /// an empty object, a single object, or an array.
    /// </summary>
    public static IEnumerable<JsonElement> Elements(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var value))
        {
            yield break;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    yield return item;
                }

                break;

            case JsonValueKind.Object when value.EnumerateObject().Any():
                yield return value;
                break;

            default:
                yield break;
        }
    }

    public static string GetString(JsonElement element, string propertyName, string fallback = "")
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    /// <summary>MFL sends numbers as strings ("101.5"); missing or blank yields null.</summary>
    public static decimal? GetDecimal(JsonElement element, string propertyName)
        => decimal.TryParse(
            GetString(element, propertyName),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    public static int? GetInt(JsonElement element, string propertyName)
        => int.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
