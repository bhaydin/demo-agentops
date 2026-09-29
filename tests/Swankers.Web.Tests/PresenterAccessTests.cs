using System.Net;
using Swankers.Web.Presenter;
using Swankers.Web.Tests.Support;

namespace Swankers.Web.Tests;

/// <summary>Nothing privileged answers without the presenter session (Codex Phase 6 P1).</summary>
public sealed class PresenterAccessTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_sign_in()
    {
        await using var host = new WebTestHost();
        host.Api.State = Fixtures.State(pendingApproval: true);

        using var page = await host.CreateClient().GetAsync("/", CT);

        Assert.Equal(HttpStatusCode.Found, page.StatusCode);
        Assert.StartsWith("http://localhost/login", page.Headers.Location?.ToString());
        Assert.Empty(host.Api.Requests);
    }

    [Fact]
    public async Task The_sign_in_page_is_public_and_health_stays_anonymous()
    {
        await using var host = new WebTestHost();
        var client = host.CreateClient();

        using var login = await client.GetAsync("/login", CT);
        var html = await login.Content.ReadAsStringAsync(CT);
        using var health = await client.GetAsync("/healthz", CT);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("name=\"key\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task The_wrong_key_does_not_sign_in()
    {
        await using var host = new WebTestHost();
        var client = host.CreateClient();

        using var attempt = await WebTestHost.SignInAsync(client, "not-the-key");
        using var page = await client.GetAsync("/", CT);

        Assert.Equal(HttpStatusCode.Found, attempt.StatusCode);
        Assert.Contains("failed=true", attempt.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Found, page.StatusCode);
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_rejected()
    {
        await using var host = new WebTestHost();

        using var attempt = await host.CreateClient().PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = WebTestHost.PresenterKey }), CT);

        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
    }

    [Fact]
    public async Task The_presenter_key_opens_the_stage_and_sign_out_closes_it()
    {
        await using var host = new WebTestHost();
        var client = host.CreateClient();

        using var signIn = await WebTestHost.SignInAsync(client, WebTestHost.PresenterKey);
        using var page = await client.GetAsync("/", CT);
        var html = await page.Content.ReadAsStringAsync(CT);
        using var signOut = await client.PostAsync("/logout", content: null, CT);
        using var after = await client.GetAsync("/", CT);

        Assert.Equal(HttpStatusCode.Found, signIn.StatusCode);
        Assert.Equal("/", signIn.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Swankers Coach", html);
        Assert.Contains("Anchorage Falling", html);
        Assert.Equal(HttpStatusCode.Found, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
    }

    [Fact]
    public void An_empty_configured_key_never_matches()
    {
        Assert.False(PresenterLogin.Matches("", ""));
        Assert.False(PresenterLogin.Matches(null, "k"));
        Assert.False(PresenterLogin.Matches("k", ""));
        Assert.False(PresenterLogin.Matches("K", "k"));
        Assert.True(PresenterLogin.Matches("k", "k"));
    }
}
