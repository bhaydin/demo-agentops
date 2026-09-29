using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Swankers.Web.Coach;
using Swankers.Web.League;

namespace Swankers.Web.Tests.Support;

/// <summary>
/// The web app in-process: real Program.cs wiring, with the league API handler and the Coach
/// replaced by fakes. Pages are fetched as prerendered HTML, which runs every component's
/// initialization (state fetch, version lookup) without a browser.
/// </summary>
public sealed class WebTestHost : IAsyncDisposable
{
    public const string AdminKey = "admin-test-key";

    private readonly WebApplicationFactory<Program> _factory;

    public WebTestHost()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:BaseUrl", "http://league.test");
            b.UseSetting("Mcp:DemoAdminKey", AdminKey);
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

    public HttpClient CreateClient() => _factory.CreateClient();

    public T Resolve<T>() where T : notnull => _factory.Services.GetRequiredService<T>();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
