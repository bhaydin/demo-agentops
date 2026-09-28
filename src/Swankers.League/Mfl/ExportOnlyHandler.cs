namespace Swankers.League.Mfl;

/// <summary>
/// Runtime backstop for AGENTS.md hard rule 1: refuses to send anything that is not an MFL
/// export request, no matter how the URL was produced. A unit test locks this behavior in;
/// do not remove or weaken either.
/// </summary>
public sealed class ExportOnlyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("MFL request has no URI.");

        if (!uri.Host.EndsWith(".myfantasyleague.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Blocked request to non-MFL host '{uri.Host}'.");
        }

        // Path segments only ("/2026/export"); the query string cannot smuggle a verb.
        var isExport = uri.Segments.Any(s =>
            s.Equals("export", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("export/", StringComparison.OrdinalIgnoreCase));

        if (!isExport)
        {
            throw new InvalidOperationException(
                "Blocked non-export MFL request. Import (write) endpoints are forbidden; " +
                "all writes go to SimLeague.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
