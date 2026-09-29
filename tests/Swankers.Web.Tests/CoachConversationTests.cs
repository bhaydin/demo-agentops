using Microsoft.Extensions.Logging.Abstractions;
using Swankers.Web.Coach;
using Swankers.Web.Tests.Support;

namespace Swankers.Web.Tests;

/// <summary>The conversation follows the routed version: a re-route ends the session (Codex Phase 6 P1).</summary>
public sealed class CoachConversationTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_route_change_ends_the_session_and_the_next_message_starts_on_the_new_version()
    {
        var coach = new FakeCoach();
        var conversation = new CoachConversation(coach, coach, NullLogger<CoachConversation>.Instance);

        await conversation.SendAsync("Who is my QB?", CT);
        var unchanged = await conversation.SyncVersionAsync(CT);
        coach.Version = coach.Version with { Version = "6", Credential = "commissioner" };
        var changed = await conversation.SyncVersionAsync(CT);
        var boundAfterChange = conversation.BoundVersion;
        await conversation.SendAsync("And now?", CT);

        Assert.False(unchanged);
        Assert.True(changed);
        Assert.Null(boundAfterChange);
        Assert.Equal("6", conversation.BoundVersion?.Version);
        Assert.Equal(2, coach.SessionsStarted);
        Assert.Equal(["Who is my QB?", "And now?"], coach.Received);
        Assert.Equal(["user", "assistant", "system", "user", "assistant"], conversation.Entries.Select(e => e.Role));
        Assert.Contains("Coach v7 (prompt v1, owner credential) was replaced by Coach v6 (prompt v1, commissioner credential)", conversation.Entries[2].Text);
    }

    [Fact]
    public async Task A_route_change_between_messages_is_caught_at_send_time()
    {
        var coach = new FakeCoach();
        var conversation = new CoachConversation(coach, coach, NullLogger<CoachConversation>.Instance);

        await conversation.SendAsync("first", CT);
        coach.Version = coach.Version with { Version = "6" };
        await conversation.SendAsync("second", CT);

        Assert.Equal(2, coach.SessionsStarted);
        Assert.Equal("6", conversation.BoundVersion?.Version);
        Assert.Contains(conversation.Entries, e => e.Role == "system" && e.Text.Contains("starts over"));
    }

    [Fact]
    public async Task Replies_stream_into_one_entry_and_a_failure_stays_in_the_conversation()
    {
        var coach = new FakeCoach();
        var conversation = new CoachConversation(coach, coach, NullLogger<CoachConversation>.Instance);
        var changes = 0;
        conversation.Changed += () => changes++;

        await conversation.SendAsync("first", CT);
        var reply = conversation.Entries.Last();
        coach.Reply = _ => throw new InvalidOperationException("endpoint unavailable");
        await conversation.SendAsync("second", CT);

        Assert.Equal("assistant", reply.Role);
        Assert.Equal("Start Judkins.", reply.Text);
        Assert.Contains("endpoint unavailable", conversation.Error);
        Assert.False(conversation.Busy);
        Assert.False(conversation.Thinking);
        Assert.Equal("7", conversation.BoundVersion?.Version);
        Assert.True(changes >= 4);
    }

    [Fact]
    public async Task Blank_messages_are_ignored_and_nothing_starts_until_the_first_message()
    {
        var coach = new FakeCoach();
        var conversation = new CoachConversation(coach, coach, NullLogger<CoachConversation>.Instance);

        await conversation.SendAsync("   ", CT);
        var synced = await conversation.SyncVersionAsync(CT);

        Assert.Empty(conversation.Entries);
        Assert.Equal(0, coach.SessionsStarted);
        Assert.Null(conversation.BoundVersion);
        Assert.False(synced);
    }
}
