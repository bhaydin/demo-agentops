namespace Swankers.League.Mfl;

/// <summary>
/// MFL answered HTTP 200 with an error envelope (<c>{"error": ...}</c>) instead of data.
/// Treated like a transport failure: never cached, served from the snapshot fallback when one
/// exists. The message carries only the request type, never MFL's text, so nothing from the
/// response can reach logs.
/// </summary>
public sealed class MflResponseException(string requestType)
    : Exception($"MFL returned an error envelope for export type '{requestType}'.")
{
    public string RequestType { get; } = requestType;
}
