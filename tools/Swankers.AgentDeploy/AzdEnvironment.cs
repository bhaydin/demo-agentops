using System.Diagnostics;
using System.Text.Json;

namespace Swankers.AgentDeploy;

/// <summary>
/// Settings the commands fall back to when an option is not given: the process environment
/// first, then the repository's selected azd environment (<c>azd env get-values</c>), so the
/// documented standalone commands work from a fresh shell after <c>azd up</c>. Values are
/// endpoints and names, never secrets (the azd environment holds none by design).
/// </summary>
public static class AzdEnvironment
{
    public static IReadOnlyDictionary<string, string> Load(string repoRoot)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ReadAzd(repoRoot))
        {
            settings[key] = value;
        }

        // Explicit process variables win over the azd environment.
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value && !string.IsNullOrWhiteSpace(value))
            {
                settings[key] = value;
            }
        }

        return settings;
    }

    /// <summary>Parses <c>azd env get-values --output json</c>: a flat object of string values.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string json)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return values;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } value)
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> ReadAzd(string repoRoot)
    {
        try
        {
            var start = new ProcessStartInfo("azd")
            {
                ArgumentList = { "env", "get-values", "--output", "json", "--cwd", repoRoot, "--no-prompt" },
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return new Dictionary<string, string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? Parse(output) : new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or JsonException)
        {
            // No azd, no environment yet, or unexpected output: the caller reports what is missing.
            return new Dictionary<string, string>();
        }
    }
}
