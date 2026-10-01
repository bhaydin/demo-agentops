namespace Swankers.AgentDeploy;

/// <summary>
/// One stage configuration of the Coach hosted agent. The four versions ARCHITECTURE.md
/// calls for differ only in prompt version, MCP credential key, and model; no value here is a secret
/// (the credential is looked up in Key Vault by the Coach at startup).
/// </summary>
public sealed record CoachVersionSpec(
    string Label,
    string PromptVersion,
    string McpCredentialKey,
    string McpEndpoint,
    string KeyVaultUri,
    string ProjectEndpoint,
    string ModelDeployment,
    string Cpu = "1",
    string Memory = "2Gi")
{
    public string CredentialName => McpCredentialKey.EndsWith("CommissionerCredential", StringComparison.OrdinalIgnoreCase)
        ? "commissioner"
        : "owner";

    public string Description => $"Coach {Label}: prompt {PromptVersion}, {CredentialName} credential, model {ModelDeployment}";

    /// <summary>Environment the hosted container starts with; see src/Swankers.Coach/Program.cs.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables => new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["FOUNDRY_PROJECT_ENDPOINT"] = ProjectEndpoint,
        ["AZURE_AI_MODEL_DEPLOYMENT_NAME"] = ModelDeployment,
        ["KeyVault__Uri"] = KeyVaultUri,
        ["Coach__McpEndpoint"] = McpEndpoint,
        ["Coach__PromptVersion"] = PromptVersion,
        ["Coach__McpCredentialKey"] = McpCredentialKey,
    };

    /// <summary>Metadata stamped on the version so `list` can tell the stages apart.</summary>
    public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>
    {
        ["stage"] = Label,
        ["prompt"] = PromptVersion,
        ["credential"] = CredentialName,
        ["model"] = ModelDeployment,
    };
}
