namespace Swankers.Coach;

/// <summary>
/// Binds the "Coach" configuration section. The three stage configurations differ only in
/// <see cref="PromptVersion"/> and <see cref="McpCredentialKey"/>; nothing here is a secret.
/// </summary>
public sealed class CoachOptions
{
    public const string SectionName = "Coach";

    /// <summary>Foundry project endpoint, e.g. https://{account}.services.ai.azure.com/api/projects/{project}. Also read from FOUNDRY_PROJECT_ENDPOINT.</summary>
    public string ProjectEndpoint { get; set; } = "";

    /// <summary>Chat model deployment name. Also read from AZURE_AI_MODEL_DEPLOYMENT_NAME.</summary>
    public string ModelDeployment { get; set; } = "gpt-5.4";

    /// <summary>Which prompts/coach-{version}.md to load: v1 (good) or v2 (the regressed tweak).</summary>
    public string PromptVersion { get; set; } = "v1";

    /// <summary>Streamable HTTP endpoint of Swankers.Mcp.</summary>
    public string McpEndpoint { get; set; } = "http://localhost:5210/mcp";

    /// <summary>
    /// Configuration key whose value is the bearer credential sent to the MCP server. Defaults to
    /// the owner credential (Key Vault Mcp--OwnerCredential). The Friday "before" version points
    /// this at Mcp:CommissionerCredential. The value itself never appears in configuration files.
    /// </summary>
    public string McpCredentialKey { get; set; } = "Mcp:OwnerCredential";

    /// <summary>Folder of markdown files the search_league_knowledge tool indexes.</summary>
    public string KnowledgeRoot { get; set; } = "knowledge";

    public string AgentName { get; set; } = "Coach";
}
