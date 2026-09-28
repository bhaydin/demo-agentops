namespace Swankers.AgentDeploy;

/// <summary>
/// Minimal <c>command --option value --flag</c> parser. Options fall back to settings: the
/// process environment and the selected azd environment (see <see cref="AzdEnvironment"/>),
/// which export FOUNDRY_PROJECT_ENDPOINT and friends after <c>azd up</c>.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _settings;

    private CommandLine(string command, IReadOnlyDictionary<string, string> settings)
    {
        Command = command;
        _settings = settings;
    }

    public string Command { get; }

    public static CommandLine Parse(string[] args, IReadOnlyDictionary<string, string>? settings = null)
    {
        var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "";
        var result = new CommandLine(command, settings ?? new Dictionary<string, string>());
        for (var i = command.Length > 0 ? 1 : 0; i < args.Length; i++)
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

    public string? Optional(string name, string? settingName = null)
    {
        if (_options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return settingName is not null && _settings.TryGetValue(settingName, out var setting) && !string.IsNullOrWhiteSpace(setting)
            ? setting
            : null;
    }

    public string Required(string name, string? settingName = null)
        => Optional(name, settingName) ?? throw new ArgumentException(
            settingName is null
                ? $"--{name} is required."
                : $"--{name} is required, or {settingName} in the process environment or the selected azd environment (run from the repository after azd up).");
}
