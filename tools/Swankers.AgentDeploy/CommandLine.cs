namespace Swankers.AgentDeploy;

/// <summary>
/// Minimal <c>command --option value --flag</c> parser. Options fall back to environment
/// variables (the azd environment exports them as FOUNDRY_PROJECT_ENDPOINT and friends).
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private init; } = "";

    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine { Command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "" };
        for (var i = result.Command.Length > 0 ? 1 : 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }

            var name = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                result._options[name] = args[++i];
            }
            else
            {
                result._flags.Add(name);
            }
        }

        return result;
    }

    public bool Flag(string name) => _flags.Contains(name);

    public string? Optional(string name, string? environmentVariable = null)
    {
        if (_options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var fromEnvironment = environmentVariable is null ? null : Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    public string Required(string name, string? environmentVariable = null)
        => Optional(name, environmentVariable) ?? throw new ArgumentException(
            environmentVariable is null
                ? $"--{name} is required."
                : $"--{name} is required (or set {environmentVariable}; azd env get-values shows it after azd up).");
}
