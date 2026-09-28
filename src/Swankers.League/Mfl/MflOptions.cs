using System.ComponentModel.DataAnnotations;

namespace Swankers.League.Mfl;

/// <summary>
/// Binds the "Mfl" configuration section. In deployed and maintainer environments the values
/// come from Key Vault secrets (Mfl--ApiKey, Mfl--LeagueId, Mfl--Host, Mfl--UserAgent);
/// locally, user-secrets may override. Never commit these values.
/// </summary>
public sealed class MflOptions
{
    public const string SectionName = "Mfl";

    /// <summary>Export-only, owner-scoped API key. Never logged, never sent to a non-MFL host.</summary>
    [Required]
    public string ApiKey { get; set; } = "";

    [Required]
    [RegularExpression("^[0-9]+$")]
    public string LeagueId { get; set; } = "";

    /// <summary>The league's own host, e.g. "www42.myfantasyleague.com" (Key Vault: Mfl--Host).</summary>
    [Required]
    [RegularExpression(@"^www\d{2}\.myfantasyleague\.com$")]
    public string Host { get; set; } = "";

    /// <summary>Host for requests that take no league id; MFL spreads these across servers.</summary>
    [RegularExpression(@"^[a-z0-9.-]+\.myfantasyleague\.com$")]
    public string ApiHost { get; set; } = "api.myfantasyleague.com";

    /// <summary>The User-Agent registered with MFL; sent on every request.</summary>
    [Required]
    public string UserAgent { get; set; } = "";

    /// <summary>MFL season year in the request path.</summary>
    [Range(2020, 2100)]
    public int Year { get; set; } = 2026;

    /// <summary>MFL asks clients to wait about one second between requests.</summary>
    public TimeSpan MinRequestSpacing { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Cache lifetime for league data (rosters, standings, ...).</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Cache lifetime for the full player list, which MFL says changes rarely.</summary>
    public TimeSpan PlayerCacheDuration { get; set; } = TimeSpan.FromHours(6);
}
