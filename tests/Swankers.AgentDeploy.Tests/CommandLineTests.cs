using Swankers.AgentDeploy;

namespace Swankers.AgentDeploy.Tests;

/// <summary>
/// Codex Phase 4 P2: the documented standalone commands must work in a fresh shell, so
/// options fall back to settings loaded from the azd environment, not only process variables.
/// </summary>
public sealed class CommandLineTests
{
    private static readonly IReadOnlyDictionary<string, string> AzdSettings = new Dictionary<string, string>
    {
        ["FOUNDRY_PROJECT_ENDPOINT"] = "https://example.services.ai.azure.com/api/projects/demo",
        ["MCP_ENDPOINT"] = "https://mcp.example/mcp",
    };

    [Fact]
    public void Required_falls_back_to_the_loaded_settings()
    {
        var line = CommandLine.Parse(["list"], AzdSettings);

        Assert.Equal("list", line.Command);
        Assert.Equal(AzdSettings["FOUNDRY_PROJECT_ENDPOINT"], line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"));
    }

    [Fact]
    public void An_explicit_option_wins_over_the_settings()
    {
        var line = CommandLine.Parse(["route", "--version", "5", "--project-endpoint", "https://other/api/projects/p"], AzdSettings);

        Assert.Equal("https://other/api/projects/p", line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"));
        Assert.Equal("5", line.Required("version"));
    }

    [Fact]
    public void A_missing_setting_names_the_variable_and_the_azd_environment()
    {
        var line = CommandLine.Parse(["list"], new Dictionary<string, string>());

        var ex = Assert.Throws<ArgumentException>(() => line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"));

        Assert.Contains("--project-endpoint", ex.Message);
        Assert.Contains("FOUNDRY_PROJECT_ENDPOINT", ex.Message);
        Assert.Contains("azd", ex.Message);
    }

    [Fact]
    public void Flags_and_options_parse_together()
    {
        var line = CommandLine.Parse(["create", "--label", "v1-owner", "--route", "--prompt", "v1"]);

        Assert.Equal("v1-owner", line.Optional("label"));
        Assert.Equal("v1", line.Optional("prompt"));
        Assert.True(line.Flag("route"));
        Assert.False(line.Flag("no-publish"));
        Assert.Null(line.Optional("cpu"));
    }

    [Fact]
    public void Azd_values_parse_from_the_json_output()
    {
        const string json = """
            {"AZURE_ENV_NAME":"swankers-dev","FOUNDRY_PROJECT_ENDPOINT":"https://x/api/projects/p","EMPTY":"","COUNT":3}
            """;

        var values = AzdEnvironment.Parse(json);

        Assert.Equal("https://x/api/projects/p", values["foundry_project_endpoint"]);
        Assert.Equal("swankers-dev", values["AZURE_ENV_NAME"]);
        Assert.False(values.ContainsKey("EMPTY"));
        Assert.False(values.ContainsKey("COUNT"));
    }

    [Fact]
    public void Azd_output_that_is_empty_or_not_an_object_yields_nothing()
    {
        Assert.Empty(AzdEnvironment.Parse(""));
        Assert.Empty(AzdEnvironment.Parse("[]"));
    }
}
