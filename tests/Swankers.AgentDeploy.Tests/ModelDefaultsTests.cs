using System.Text.RegularExpressions;

namespace Swankers.AgentDeploy.Tests;

/// <summary>
/// Codex Phase 7 P1: deploy-coach.ps1 defaulted to a model the shared Foundry module does not
/// deploy, so following the README produced a Friday "before" version that could not answer.
/// The script's default and the Bicep module's extraModels default must name the same model.
/// </summary>
public sealed class ModelDefaultsTests
{
    [Fact]
    public void The_deploy_script_and_the_foundry_module_agree_on_the_before_model()
    {
        var checkout = FindCheckout();
        var script = File.ReadAllText(Path.Combine(checkout, "tools", "Swankers.AgentDeploy", "deploy-coach.ps1"));
        var module = File.ReadAllText(Path.Combine(checkout, "infra", "modules", "foundry.bicep"));

        var scriptDefault = Regex.Match(script, @"\$BeforeModel\s*=\s*'([^']+)'").Groups[1].Value;
        var moduleDefault = Regex.Match(module, @"param extraModels array = \[\s*\{\s*name:\s*'([^']+)'", RegexOptions.Singleline).Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(scriptDefault), "deploy-coach.ps1 has no $BeforeModel default");
        Assert.False(string.IsNullOrEmpty(moduleDefault), "foundry.bicep has no extraModels default");
        Assert.Equal(moduleDefault, scriptDefault);
    }

    /// <summary>
    /// The Friday "contained" run holds the "before" prompt and model and changes only the
    /// credential; if the stage drifts, the middle run stops isolating the architecture.
    /// </summary>
    [Fact]
    public void The_contained_stage_keeps_the_before_prompt_and_model_with_the_owner_credential()
    {
        // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
        var checkout = FindCheckout();
        var script = File.ReadAllText(Path.Combine(checkout, "tools", "Swankers.AgentDeploy", "deploy-coach.ps1"));
        var presets = File.ReadAllText(Path.Combine(checkout, "demo", "stage.ps1"));

        var stage = Regex.Match(script, @"@\{\s*Label\s*=\s*'v0-owner'[^}]*\}").Value;
        var preset = Regex.Match(presets, @"'friday-contained'\s*=\s*@\{[^}]*\}").Value;

        Assert.False(string.IsNullOrEmpty(stage), "deploy-coach.ps1 has no v0-owner stage");
        Assert.Matches(@"Prompt\s*=\s*'v0'", stage);
        Assert.Matches(@"Credential\s*=\s*'Mcp:OwnerCredential'", stage);
        Assert.Matches(@"Model\s*=\s*\$BeforeModel\b", stage);
        Assert.Contains("$versions['v1-owner']", script); // the routed default stays the hardened version
        Assert.Matches(@"Label\s*=\s*'v0-owner'", preset);
        Assert.Matches(@"Credential\s*=\s*'owner'", preset);
    }

    private static string FindCheckout()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwankersCoach.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("SwankersCoach.slnx not found above the test binaries.");
    }
}
