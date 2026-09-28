using System.Collections.Concurrent;
using Azure.Core;

namespace Swankers.Coach;

/// <summary>
/// Caches access tokens per scope set in front of another credential. DefaultAzureCredential
/// on a developer machine ends at the Azure CLI, which shells out on every request (about
/// 3.5 s per model call in the Phase 3 trace); tokens are valid for an hour, so reuse them.
/// Managed identity in the hosted runtime is already fast; the cache is harmless there.
/// </summary>
public sealed class CachedTokenCredential(TokenCredential inner, TimeProvider? time = null) : TokenCredential
{
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new(StringComparer.Ordinal);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var key = Key(requestContext);
        if (_tokens.TryGetValue(key, out var cached) && IsFresh(cached))
        {
            return cached;
        }

        var token = inner.GetToken(requestContext, cancellationToken);
        _tokens[key] = token;
        return token;
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var key = Key(requestContext);
        if (_tokens.TryGetValue(key, out var cached) && IsFresh(cached))
        {
            return cached;
        }

        var token = await inner.GetTokenAsync(requestContext, cancellationToken);
        _tokens[key] = token;
        return token;
    }

    private bool IsFresh(AccessToken token) => token.ExpiresOn - _time.GetUtcNow() > RefreshBefore;

    private static string Key(TokenRequestContext context)
        => string.Join(' ', context.Scopes) + "|" + (context.TenantId ?? "");
}
