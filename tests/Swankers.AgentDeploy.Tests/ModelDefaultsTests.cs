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
