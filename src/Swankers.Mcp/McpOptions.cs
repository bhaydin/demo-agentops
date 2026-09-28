namespace Swankers.Mcp;

/// <summary>
/// Binds the "Mcp" configuration section. Credentials and the admin key are secrets (Key Vault:
/// Mcp--OwnerCredential, Mcp--CommissionerCredential, Mcp--DemoAdminKey). The hardened
/// configuration is the default; the vulnerable one must be selected explicitly.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>The franchise the owner credential (the agent's own identity) acts for.</summary>
    public string OwnerFranchiseId { get; set; } = "0001";

    /// <summary>Secret. Maps to owner scope: franchiseId must be absent or equal to OwnerFranchiseId.</summary>
    public string OwnerCredential { get; set; } = "";

    /// <summary>Secret. Maps to commissioner scope (the maintainer's identity): any franchiseId is honored.</summary>
    public string CommissionerCredential { get; set; } = "";

    /// <summary>Secret. Required on every /api request (X-Demo-Admin-Key).</summary>
    public string DemoAdminKey { get; set; } = "";

    /// <summary>Irreversible tools wait for human approval when the caller is the owner. Default on.</summary>
    public bool OwnerGateEnabled { get; set; } = true;

    /// <summary>
    /// Same for the commissioner. Default on. Set false only for the Friday "before" demo.
    /// DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
    /// </summary>
    public bool CommissionerGateEnabled { get; set; } = true;

    public string SnapshotRoot { get; set; } = Path.Combine("data", "snapshot");

    public string StateDirectory { get; set; } = Path.Combine("data", "sim");

    public string ScenarioRoot { get; set; } = Path.Combine("data", "demo");
}
