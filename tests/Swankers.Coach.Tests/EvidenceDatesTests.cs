using System.Text.Json;
using Microsoft.Extensions.AI;
using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The date rule for the workplace scenario: the business date an item states is authoritative for
/// chronology, and mailbox received time is transport metadata only. Live and Replay apply it
/// the same way because the rule lives on the evidence item, not in either provider.
/// </summary>
public class EvidenceDatesTests
{
    private static readonly DateTimeOffset SeedingDay = new(2026, 10, 5, 19, 10, 24, TimeSpan.Zero);

    [Theory]
    [InlineData("Kickoff notes, September 22, 2026.\nPlanned pilot go-live: November 2, 2026.", 2026, 9, 22)]
    [InlineData("October 1, 2026.\nDana Okafor approved moving the pilot go-live from November 2 to November 16, 2026.", 2026, 10, 1)]
    [InlineData("September 30, 2026.\r\nAction: deliver the security review summary by October 9, 2026.", 2026, 9, 30)]
    public void The_business_date_is_the_first_date_the_text_states(string text, int year, int month, int day)
        => Assert.Equal(new DateOnly(year, month, day), EvidenceDates.FirstStatedDate(text));

    [Theory]
    [InlineData("Marcus Lindqvist will send the revised statement of work to Dana Okafor.")]
    [InlineData("Due 10/09/2026, or perhaps Oct 9.")]   // only the written-out form is the corpus convention
    [InlineData("February 30, 2026 is not a date.")]
    [InlineData("")]
    public void Text_that_states_no_date_has_no_business_date(string text)
        => Assert.Null(EvidenceDates.FirstStatedDate(text));

    [Fact]
    public void Received_time_never_becomes_the_business_date()
    {
        var undated = Item("E1", "A note with no date in it.", SeedingDay);

        Assert.Null(undated.BusinessDate);
        Assert.Equal(SeedingDay, undated.ReceivedAt);
    }

    [Fact]
    public void Live_and_replay_items_with_the_same_text_have_the_same_business_date()
    {
        const string text = "October 1, 2026.\nDana Okafor approved moving the pilot go-live from November 2 to November 16, 2026.";
        var live = Item("E5", text, SeedingDay);                                              // received on the seeding day
        var replay = Item("E5", text, new DateTimeOffset(2026, 10, 5, 14, 18, 0, TimeSpan.FromHours(-5))) with { Synthetic = true };

        Assert.Equal(new DateOnly(2026, 10, 1), live.BusinessDate);
        Assert.Equal(live.BusinessDate, replay.BusinessDate);
    }

    [Fact]
    public void Received_order_does_not_decide_which_fact_is_latest()
    {
        // The superseding decision arrives first and the superseded note last: by received time the
        // old date would look newest. By business date the decision is still the later one.
        var decision = Item("E1", "October 1, 2026.\nGo-live moved to November 16, 2026.", SeedingDay);
        var superseded = Item("E2", "Kickoff notes, September 22, 2026.\nPlanned go-live: November 2, 2026.", SeedingDay.AddMinutes(30));

        Assert.True(superseded.ReceivedAt > decision.ReceivedAt);
        Assert.Equal("E1", new[] { decision, superseded }.MaxBy(i => i.BusinessDate)!.Id);
    }

    [Fact]
    public async Task The_model_is_given_both_dates_under_names_that_say_what_they_are()
    {
        var provider = new FixedProvider([Item("E1", "Kickoff notes, September 22, 2026.\nPlanned pilot go-live: November 2, 2026.", SeedingDay)]);
        var tool = (AIFunction)new EngagementEvidenceTool(provider, new WorkplaceOptions(), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(-5))).AsTool();

        var json = JsonSerializer.Serialize(await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("\"businessDate\":\"2026-09-22\"", json.Replace(" ", ""));
        Assert.Contains("\"receivedAt\":\"2026-10-05T19:10:24", json.Replace(" ", ""));
        Assert.DoesNotContain("\"timestamp\"", json);
        Assert.Contains("businessDate (the date the evidence itself states", tool.Description);
        Assert.Contains("transport metadata only", tool.Description);
    }

    private static EvidenceItem Item(string id, string text, DateTimeOffset receivedAt)
        => new(id, "email", "[NW-DPM] Item", "Someone", receivedAt, text, $"ref-{id}", null);

    private sealed class FixedProvider(IReadOnlyList<EvidenceItem> items) : IWorkplaceContextProvider
    {
        public string Mode => WorkplaceModes.Live;

        public string? Fixture => null;

        public Task<IReadOnlyList<EvidenceItem>> GetEvidenceAsync(CancellationToken cancellationToken) => Task.FromResult(items);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
