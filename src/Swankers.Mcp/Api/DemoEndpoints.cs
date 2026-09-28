using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Swankers.League.Sim;
using Swankers.Mcp.Gate;
using Swankers.Mcp.Scenarios;
using Swankers.Mcp.Tools;

namespace Swankers.Mcp.Api;

public sealed record ConfirmationDecision(bool Approve);

/// <summary>
/// Demo REST surface for the web app and the reset script. Protected by the demo admin key.
/// Approval of gated actions lives ONLY here; it is never exposed as an MCP tool.
/// </summary>
public static class DemoEndpoints
{
    public const string AdminKeyHeader = "X-Demo-Admin-Key";

    public static IEndpointRouteBuilder MapDemoApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<AdminKeyFilter>();

        api.MapGet("/state", async (
            SimLeague sim, LeagueViews views, ConfirmationGate gate, IOptions<McpOptions> options, CancellationToken ct)
            => Results.Ok(await views.StateAsync(options.Value.OwnerFranchiseId, gate, ct)));

        api.MapGet("/confirmations", (ConfirmationGate gate)
            => Results.Ok(new { pending = gate.Pending, recent = gate.Recent }));

        api.MapPost("/confirmations/{id}", async (
            string id, ConfirmationDecision decision, ConfirmationGate gate, CancellationToken ct) =>
        {
            using var activity = McpDiagnostics.ActivitySource.StartActivity("confirmation.resolve");
            activity?.SetTag("swankers.confirmation.id", id);
            activity?.SetTag("swankers.confirmation.approved", decision.Approve);
            var outcome = await gate.ResolveAsync(id, decision.Approve, ct);
            return outcome is null ? Results.NotFound(new { error = $"No pending confirmation '{id}'." }) : Results.Ok(outcome);
        });

        api.MapPost("/admin/reset", async (SimLeague sim, ConfirmationGate gate, CancellationToken ct) =>
        {
            await sim.ResetAsync(null, ct);
            gate.Clear();
            return Results.Ok(new { status = "reset" });
        });

        api.MapPost("/admin/seed/{scenario}", async (string scenario, ScenarioSeeder seeder, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await seeder.SeedAsync(scenario, ct));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}

/// <summary>Constant-time check of the demo admin key. No key configured means nothing is allowed.</summary>
public sealed class AdminKeyFilter(IOptions<McpOptions> options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = options.Value.DemoAdminKey;
        var presented = context.HttpContext.Request.Headers[DemoEndpoints.AdminKeyHeader].ToString();
        var ok = expected.Length > 0 &&
                 CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));

        return ok ? next(context) : ValueTask.FromResult<object?>(Results.Unauthorized());
    }
}
