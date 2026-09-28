using Azure.Core;

namespace Swankers.Coach.Tests;

public class CachedTokenCredentialTests
{
    private sealed class CountingCredential(Func<DateTimeOffset> expiresOn) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return new AccessToken($"token-{Calls}", expiresOn());
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    [Fact]
    public async Task Reuses_a_token_until_it_is_about_to_expire()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var inner = new CountingCredential(() => clock.Now.AddHours(1));
        var credential = new CachedTokenCredential(inner, clock);
        var scopes = new TokenRequestContext(["https://ai.azure.com/.default"]);

        var first = await credential.GetTokenAsync(scopes, CancellationToken.None);
        var second = await credential.GetTokenAsync(scopes, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(56)); // inside the 5-minute refresh window
        var third = await credential.GetTokenAsync(scopes, CancellationToken.None);

        Assert.Equal("token-1", first.Token);
        Assert.Equal("token-1", second.Token);
        Assert.Equal("token-2", third.Token);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void Caches_per_scope_set()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var inner = new CountingCredential(() => clock.Now.AddHours(1));
        var credential = new CachedTokenCredential(inner, clock);

        credential.GetToken(new TokenRequestContext(["https://ai.azure.com/.default"]), CancellationToken.None);
        credential.GetToken(new TokenRequestContext(["https://search.azure.com/.default"]), CancellationToken.None);
        credential.GetToken(new TokenRequestContext(["https://ai.azure.com/.default"]), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Minimal manual clock so the test controls expiry without a timer package.</summary>
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan by) => Now += by;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
