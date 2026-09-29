using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    /// <summary>
    /// Resolves and confirms. A timeout, a transport error, or an unreadable response does not
    /// mean the decision was lost: the request may well have executed, so the league state is
    /// re-read and the confirmation's recorded outcome wins. Only when the league still shows it
    /// pending is the failure reported, for a retry. Never throws for those failures.
    /// </summary>
    public async Task<DecisionResult> DecideAsync(string confirmationId, bool approve, CancellationToken ct)
    {
        try
        {
            var outcome = await ResolveAsync(confirmationId, approve, ct);
            if (outcome is not null)
            {
                return DecisionResult.Resolved(outcome);
            }

            var recorded = await RecordedOutcomeAsync(confirmationId, ct);
            return recorded is not null
                ? DecisionResult.Resolved(recorded, reconciled: true)
                : DecisionResult.Failed("The confirmation is no longer pending and the league has no record of it.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            var recorded = await RecordedOutcomeAsync(confirmationId, ct);
            return recorded is not null
                ? DecisionResult.Resolved(recorded, reconciled: true)
                : DecisionResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<ApprovalOutcome?> RecordedOutcomeAsync(string confirmationId, CancellationToken ct)
    {
        try
        {
            var state = await GetStateAsync(ct);
            var recorded = state.RecentConfirmations.FirstOrDefault(r => r.Confirmation.Id == confirmationId);
            return recorded is null ? null : new ApprovalOutcome(recorded.Approved, recorded.Status, null, recorded.Error);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// A decision as the league recorded it (<see cref="Outcome"/>), or why it could not be confirmed
/// (<see cref="Error"/>, with the action still pending). <see cref="Reconciled"/> means the call
/// itself failed and the outcome was read back from the league state.
/// </summary>
public sealed record DecisionResult(ApprovalOutcome? Outcome, bool Reconciled, string? Error)
{
    public static DecisionResult Resolved(ApprovalOutcome outcome, bool reconciled = false) => new(outcome, reconciled, null);

    public static DecisionResult Failed(string error) => new(null, false, error);
}
