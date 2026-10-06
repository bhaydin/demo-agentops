using System.Text.RegularExpressions;

namespace Swankers.Coach.Workplace;

/// <summary>
/// Which Work IQ tools the provider may call: default deny, exact names only, and never a tool
/// that creates, changes, deletes, or sends, even when it is listed and even when tenant policy
/// would permit it. The workplace scenario is read-only.
/// </summary>
public static partial class WorkIqToolPolicy
{
    // "accept" covers accept_eula, which the pinned server lists: an agent never accepts terms.
    private static readonly HashSet<string> MutationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "update", "delete", "action", "send", "write", "remove", "move", "copy", "patch", "reply", "forward", "accept",
    };

    /// <summary>True when any word of the tool name is a mutation verb (<c>do_action</c>, <c>createEntity</c>).</summary>
    public static bool IsMutation(string toolName)
        => WordBoundary().Split(toolName).Any(MutationWords.Contains);

    public static bool IsAllowed(string toolName, IEnumerable<string> allowList)
        => !IsMutation(toolName) && allowList.Contains(toolName, StringComparer.Ordinal);

    [GeneratedRegex(@"[^A-Za-z0-9]+|(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
