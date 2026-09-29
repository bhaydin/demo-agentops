namespace Swankers.Web.Coach;

/// <summary>A line in the conversation: user, assistant (text grows while streaming), or system (a note from the page).</summary>
public sealed class ChatEntry(string role, string text)
{
    public string Role { get; } = role;

    public string Text { get; set; } = text;
}

/// <summary>
/// One browser circuit's conversation with the Coach, bound to the agent version that was
/// routed when its session began. A Foundry session belongs to that version for good, so when
/// the endpoint is re-routed (Thursday's rollback, Friday's before/after) the conversation ends,
/// the pane says so, and the next message starts a new session on the new version. The header
/// shows the routed version; the pane shows the version the conversation is bound to; they can
/// differ only until the next message (Codex Phase 6).
/// </summary>
public sealed class CoachConversation(ICoachChat chat, IAgentVersionInfo versions, ILogger<CoachConversation> logger)
{
    private readonly List<ChatEntry> _entries = [];
    private CoachSession? _session;

    public IReadOnlyList<ChatEntry> Entries => _entries;

    /// <summary>The version the current session was created for; null when no session is open.</summary>
    public AgentVersionSummary? BoundVersion { get; private set; }

    public bool Busy { get; private set; }

    /// <summary>A message was sent and no reply text has arrived yet.</summary>
    public bool Thinking { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Raised on any change, possibly from a background thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Ends the session when the routed version differs from the one it was created for (not
    /// while a reply is streaming). Returns true when it did.
    /// </summary>
    public async Task<bool> SyncVersionAsync(CancellationToken ct)
    {
        if (_session is null || Busy)
        {
            return false;
        }

        var current = await versions.GetAsync(ct);
        if (!RoutedElsewhere(current))
        {
            return false;
        }

        EndSession(current);
        Changed?.Invoke();
        return true;
    }

    public async Task SendAsync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0 || Busy)
        {
            return;
        }

        Busy = true;
        Thinking = true;
        Error = null;
        _entries.Add(new ChatEntry("user", text));
        Changed?.Invoke();

        var reply = new ChatEntry("assistant", "");
        try
        {
            var current = await versions.GetAsync(ct);
            if (_session is not null && RoutedElsewhere(current))
            {
                EndSession(current);
            }

            if (_session is null)
            {
                _session = await chat.StartSessionAsync(ct);
                BoundVersion = current;
                logger.LogInformation("Coach conversation started on {Version}.", Describe(current));
                Changed?.Invoke();
            }

            await foreach (var chunk in chat.StreamAsync(_session, text, ct))
            {
                if (Thinking)
                {
                    Thinking = false;
                    _entries.Add(reply);
                }

                reply.Text += chunk;
                Changed?.Invoke();
            }

            if (Thinking)
            {
                reply.Text = "(no answer)";
                _entries.Add(reply);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The circuit ended mid-reply.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Coach chat failed.");
            Error = $"Coach did not answer: {ex.Message}";
        }
        finally
        {
            Thinking = false;
            Busy = false;
            Changed?.Invoke();
        }
    }

    public static string Describe(AgentVersionSummary version)
    {
        var name = version.Version is null ? version.AgentName : $"{version.AgentName} v{version.Version}";
        return version.PromptVersion is null ? name : $"{name} (prompt {version.PromptVersion}, {version.Credential} credential)";
    }

    private bool RoutedElsewhere(AgentVersionSummary current)
        => current.Version is not null && !string.Equals(current.Version, BoundVersion?.Version, StringComparison.Ordinal);

    private void EndSession(AgentVersionSummary current)
    {
        var previous = BoundVersion;
        _session = null;
        BoundVersion = null;
        _entries.Add(new ChatEntry("system", $"{(previous is null ? "Coach" : Describe(previous))} was replaced by {Describe(current)}. The conversation starts over on the new version."));
        logger.LogInformation("Coach conversation ended: routed version changed to {Version}.", current.Version);
    }
}
