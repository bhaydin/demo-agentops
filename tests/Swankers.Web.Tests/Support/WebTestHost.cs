using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Swankers.Web.Coach;
using Swankers.Web.League;

namespace Swankers.Web.Tests.Support;

/// <summary>
/// The web app in-process: real Program.cs wiring (including presenter sign-in), with the league
/// API handler and the Coach replaced by fakes. Pages are fetched as prerendered HTML, which
/// runs every component's initialization (state fetch, version lookup) without a browser.
/// </summary>
public sealed class WebTestHost : IAsyncDisposable
{
    public const string AdminKey = "admin-test-key";
    public const string PresenterKey = "presenter-test-key";

    private static readonly Regex TokenField = new("name=\"(?<name>__RequestVerificationToken)\" value=\"(?<value>[^\"]+)\"", RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public WebTestHost()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:BaseUrl", "http://league.test");
            b.UseSetting("Mcp:DemoAdminKey", AdminKey);
            b.UseSetting("Web:PresenterKey", PresenterKey);
            b.UseSetting("Coach:ProjectEndpoint", "https://foundry.test/api/projects/test");
            b.UseSetting("Coach:AgentName", "Coach");
            b.ConfigureTestServices(services =>
            {
                services.AddHttpClient<LeagueStateClient>().ConfigurePrimaryHttpMessageHandler(() => Api);
                services.RemoveAll<ICoachChat>();
                services.AddSingleton<ICoachChat>(Coach);
                services.RemoveAll<IAgentVersionInfo>();
                services.AddSingleton<IAgentVersionInfo>(Coach);
            });
        });
    }

    public FakeLeagueApi Api { get; } = new();

    public FakeCoach Coach { get; } = new();

    /// <summary>An anonymous client that does not follow redirects, so a sign-in redirect is visible.</summary>
    public HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client signed in as the presenter through the real /login form (cookies kept by the client).</summary>
    public async Task<HttpClient> CreatePresenterClientAsync(string? key = null)
    {
        var client = CreateClient();
        await SignInAsync(client, key ?? PresenterKey);
        return client;
    }

    /// <summary>Posts the sign-in form with the antiforgery token from the page; returns the response.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string key)
    {
        var page = await client.GetStringAsync("/login");
        var token = TokenField.Match(page);
        if (!token.Success)
        {
            throw new InvalidOperationException("The sign-in page has no antiforgery token.");
        }

        return await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [token.Groups["name"].Value] = token.Groups["value"].Value,
            ["key"] = key,
        }));
    }

    public T Resolve<T>() where T : notnull => _factory.Services.GetRequiredService<T>();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
