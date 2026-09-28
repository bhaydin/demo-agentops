using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Swankers.Coach.Mcp;

/// <summary>
/// Connects to Swankers.Mcp with the configured credential and exposes its tools as AITools.
/// McpClientTool is an AIFunction, so the agent calls league tools like any other function;
/// the credential, not the agent, decides what those calls may do.
/// </summary>
public sealed class McpToolSource(
    CoachOptions options,
    string credential,
    ILoggerFactory loggerFactory,
    HttpClient? httpClient = null) : IAsyncDisposable
{
    private McpClient? _client;

    public async Task<IReadOnlyList<AITool>> ConnectAsync(CancellationToken cancellationToken)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.McpEndpoint),
            Name = "swankers-league",
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {credential}" },
        };

        // An injected HttpClient lets tests point at an in-memory MCP server.
        var transport = httpClient is null
            ? new HttpClientTransport(transportOptions, loggerFactory)
            : new HttpClientTransport(transportOptions, httpClient, loggerFactory, false);

        _client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ClientInfo = new Implementation { Name = "swankers-coach", Version = "0.3.0" } },
            loggerFactory,
            cancellationToken);

        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return [.. tools.Cast<AITool>()];
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
