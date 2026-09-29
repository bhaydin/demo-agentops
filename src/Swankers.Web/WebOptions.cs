namespace Swankers.Web;

/// <summary>Binds the "Mcp" section: where the demo REST API is and the key it requires (Key Vault: Mcp--DemoAdminKey).</summary>
public sealed class McpApiOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Base URL of Swankers.Mcp (the REST API is under /api). Bicep sets Mcp__BaseUrl.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5210";

    /// <summary>Secret. Sent as X-Demo-Admin-Key on every request.</summary>
    public string DemoAdminKey { get; set; } = "";
}

/// <summary>
/// Binds the "Web" section. The page acts with the server's Foundry identity and the demo admin
/// key, so it is for the presenter only: one key (Key Vault: Web--PresenterKey), entered once at
/// /login, then a cookie session.
/// </summary>
public sealed class PresenterOptions
{
    public const string SectionName = "Web";

    /// <summary>Secret. Required; an empty key never matches.</summary>
    public string PresenterKey { get; set; } = "";
}

/// <summary>Binds the "Coach" section: which hosted agent the chat talks to and where the header reads its version from.</summary>
public sealed class CoachEndpointOptions
{
    public const string SectionName = "Coach";

    /// <summary>Foundry project endpoint, e.g. https://{account}.services.ai.azure.com/api/projects/{project}.</summary>
    public string ProjectEndpoint { get; set; } = "";

    public string AgentName { get; set; } = "Coach";

    /// <summary>
    /// The agent's endpoint URL. Which version answers is decided server side by the endpoint's
    /// version selector (AgentDeploy route), so the web app follows a rollback without redeploying.
    /// </summary>
    public Uri AgentEndpoint => new($"{ProjectEndpoint.TrimEnd('/')}/agents/{AgentName}/endpoint/protocols/openai");
}
