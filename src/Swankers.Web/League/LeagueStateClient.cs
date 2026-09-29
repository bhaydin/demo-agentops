using System.Net;
using System.Net.Http.Json;

namespace Swankers.Web.League;

/// <summary>
/// The MCP server's demo REST API as the web app uses it: the ticker state and the approval
/// decision. Approval lives only here (never as an MCP tool), so this client is the one place a
/// human's decision enters the system. The HttpClient carries the base address and the demo
/// admin key (Program.cs).
/// </summary>
public sealed class LeagueStateClient(HttpClient http)
{
    public async Task<LeagueState> GetStateAsync(CancellationToken ct)
        => await http.GetFromJsonAsync<LeagueState>("/api/state", ct) ?? LeagueState.Empty;

    /// <summary>Approves or denies a pending confirmation; null when it is no longer pending.</summary>
    public async Task<ApprovalOutcome?> ResolveAsync(string confirmationId, bool approve, CancellationToken ct)
    {
        using var activity = WebDiagnostics.ActivitySource.StartActivity("web.confirmation.resolve");
        activity?.SetTag("swankers.confirmation.id", confirmationId);
        activity?.SetTag("swankers.confirmation.approved", approve);

        using var response = await http.PostAsJsonAsync(
            $"/api/confirmations/{Uri.EscapeDataString(confirmationId)}", new { approve }, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalOutcome>(ct);
    }
}
