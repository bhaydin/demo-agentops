namespace Swankers.Coach.Tests;

/// <summary>
/// Where the live smoke test writes what Work IQ returned. Raw mailbox captures never go under
/// the checkout, ignored by git or not: the checkout may sit in a folder that syncs to a cloud
/// drive. They go to the machine's local application data instead, and WORKIQ_CAPTURE_ROOT moves
/// them for troubleshooting. Only redacted summaries (demo/winet-workiq-evidence.md) are committed.
/// </summary>
internal static class LiveCaptures
{
    public const string RootVariable = "WORKIQ_CAPTURE_ROOT";

    public static string DefaultRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwankersCoach", "workplace", "live");

    public static string Root(string? configured)
        => string.IsNullOrWhiteSpace(configured) ? DefaultRoot : Path.GetFullPath(configured);

    public static string Root() => Root(Environment.GetEnvironmentVariable(RootVariable));

    /// <summary>Writes one capture and returns its path.</summary>
    public static string Write(string name, string content, string? root = null)
    {
        var directory = root ?? Root();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
