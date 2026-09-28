namespace Swankers.Mcp.Security;

/// <summary>
/// Guards the MCP endpoint: every request needs a known bearer credential. The resolved scope
/// is stashed on the request for <see cref="CallerScopeAccessor"/>. Logs the scope kind only.
/// </summary>
public sealed class CredentialMiddleware(
    RequestDelegate next,
    CredentialResolver resolver,
    ILogger<CredentialMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var scope = resolver.Resolve(context.Request);
        if (scope is null)
        {
            logger.LogWarning("MCP request rejected: missing or unknown credential.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        context.Items[CallerScopeAccessor.ItemKey] = scope;
        await next(context);
    }
}

/// <summary>Reads the scope the middleware attached to the current request.</summary>
public sealed class CallerScopeAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string ItemKey = "Swankers.CallerScope";

    public CallerScope Current
        => httpContextAccessor.HttpContext?.Items[ItemKey] as CallerScope
            ?? throw new InvalidOperationException("No caller scope on the current request.");
}
