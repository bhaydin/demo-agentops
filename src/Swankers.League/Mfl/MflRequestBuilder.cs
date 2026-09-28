namespace Swankers.League.Mfl;

/// <summary>
/// The only place in the codebase that composes MFL URLs. Every URL it can produce is an
/// <c>export</c> request (AGENTS.md hard rule 1); there is deliberately no way to build an
/// import URL. <see cref="ExportOnlyHandler"/> enforces the same rule again at send time.
/// </summary>
public sealed class MflRequestBuilder(MflOptions options)
{
    /// <summary>Request types that take no league id; they go to the api host.</summary>
    public Uri Players() => Api("players");

    public Uri Injuries(int? week) => Api("injuries", week is null ? null : $"W={week}");

    /// <summary>League setup. Parsed for franchise ids only; owner fields are never read.</summary>
    public Uri League() => LeagueRequest("league");

    public Uri Rosters() => LeagueRequest("rosters");

    public Uri Schedule(int week) => LeagueRequest("schedule", $"W={week}");

    public Uri ProjectedScores(int week) => LeagueRequest("projectedScores", $"W={week}");

    public Uri LeagueStandings() => LeagueRequest("leagueStandings");

    public Uri Transactions(int count) => LeagueRequest("transactions", $"COUNT={count}");

    public Uri PendingTrades() => LeagueRequest("pendingTrades");

    private Uri Api(string type, string? args = null)
        => Compose(options.ApiHost, type, args, withCredentials: false);

    private Uri LeagueRequest(string type, string? args = null)
        => Compose(options.LeagueHost, type, $"L={options.LeagueId}{(args is null ? "" : "&" + args)}", withCredentials: true);

    private Uri Compose(string host, string type, string? args, bool withCredentials)
    {
        // Hard-coded "/export": this builder cannot express an import request.
        var url = $"https://{host}/{options.Year}/export?TYPE={type}";
        if (!string.IsNullOrEmpty(args))
        {
            url += $"&{args}";
        }

        if (withCredentials)
        {
            // Owner-scoped, export-only key. URLs containing it must never be logged.
            url += $"&APIKEY={Uri.EscapeDataString(options.ApiKey)}";
        }

        return new Uri(url + "&JSON=1");
    }
}
