using Swankers.Coach.Workplace;

namespace Swankers.Coach.Tests;

/// <summary>
/// The mapper against the fetch result shape captured from the pinned Work IQ server on
/// 2026-10-05 (structure only; see demo/winet-workiq-evidence.md). The values here are synthetic
/// and say so in their ids and links; they are not captures.
/// </summary>
public class WorkIqEvidenceMapperTests
{
    internal const string FetchResult = """
        {"results":[{"data":{"@odata.context":"synthetic","value":[
          {"@odata.etag":"synthetic-etag-2","id":"synthetic-msg-2","receivedDateTime":"2026-10-01T14:00:00Z","subject":"[NW-DPM] Go-live moved",
           "bodyPreview":"Approved: go-live moves to Nov 16.","webLink":"https://example.invalid/mail/2",
           "from":{"emailAddress":{"name":"Demo Owner","address":"owner@example.invalid"}}},
          {"@odata.etag":"synthetic-etag-1","id":"synthetic-msg-1","receivedDateTime":"2026-09-29T15:00:00Z","subject":"[NW-DPM] Steering recap",
           "bodyPreview":"Decision: pilot scope is limited to the finance data mart.",
           "from":{"emailAddress":{"address":"owner@example.invalid"}}},
          {"@odata.etag":"synthetic-etag-3","id":"synthetic-msg-3","receivedDateTime":"2026-10-02T12:00:00Z","subject":"Lunch on Friday?",
           "bodyPreview":"Unrelated mail that a broad query would also return.","webLink":"https://example.invalid/mail/3"}
        ]},"statusCode":200}],"note":"Results limited to 25 items per collection."}
        """;

    [Fact]
    public void Maps_graph_messages_and_keeps_their_references_as_returned()
    {
        var items = WorkIqEvidenceMapper.Map(FetchResult);

        Assert.Equal(3, items.Count);
        var moved = items[0];
        Assert.Equal(WorkIqEvidenceMapper.EmailSourceType, moved.SourceType);
        Assert.Equal("[NW-DPM] Go-live moved", moved.Title);
        Assert.Equal("Demo Owner", moved.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero), moved.ReceivedAt);   // transport metadata
        Assert.Null(moved.BusinessDate);   // this sample's text states no date, and none is borrowed from the mailbox
        Assert.Equal("Approved: go-live moves to Nov 16.", moved.Text);
        Assert.Equal("synthetic-msg-2", moved.SourceRef);
        Assert.Equal("https://example.invalid/mail/2", moved.SourceUrl);
        Assert.False(moved.Synthesized);
        Assert.False(moved.Synthetic);
    }

    [Fact]
    public void Absent_fields_stay_absent()
    {
        var recap = WorkIqEvidenceMapper.Map(FetchResult)[1];

        Assert.Equal("owner@example.invalid", recap.Author);               // no display name returned
        Assert.Equal("Decision: pilot scope is limited to the finance data mart.", recap.Text);
        Assert.Equal("synthetic-msg-1", recap.SourceRef);
        Assert.Null(recap.SourceUrl);                                       // no link returned, none invented
    }

    [Fact]
    public void A_full_body_wins_over_the_preview_when_the_query_selects_one()
    {
        var item = Assert.Single(WorkIqEvidenceMapper.Map(
            """{"results":[{"data":{"value":[{"id":"synthetic-msg-4","subject":"[NW-DPM] Notes","bodyPreview":"First line","body":{"contentType":"text","content":"First line\nSecond line"}}]},"statusCode":200}]}"""));

        Assert.Equal("First line\nSecond line", item.Text);
    }

    [Theory]
    [InlineData("""{"response":"You decided to limit the pilot scope.","conversationId":"synthetic-conversation"}""")]
    [InlineData("You decided to limit the pilot scope.")]
    public void An_answer_is_marked_synthesized_and_carries_no_reference(string raw)
    {
        var answer = Assert.Single(WorkIqEvidenceMapper.Map(raw));

        Assert.True(answer.Synthesized);
        Assert.Equal(WorkIqEvidenceMapper.SynthesizedAnswerSourceType, answer.SourceType);
        Assert.Equal("You decided to limit the pilot scope.", answer.Text);
        Assert.Null(answer.SourceRef);
        Assert.Null(answer.SourceUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"results":[{"data":{"value":[]},"statusCode":200}],"note":"Results limited to 25 items per collection."}""")]
    [InlineData("""{"paths":[{"path":"/me/messages","operations":["fetch"]}]}""")]
    public void A_result_with_no_evidence_maps_to_nothing(string raw)
        => Assert.Empty(WorkIqEvidenceMapper.Map(raw));

    [Theory]
    [InlineData("""{"results":[{"data":{"error":{"code":"ErrorAccessDenied"}},"statusCode":403}]}""", 403)]
    [InlineData("""{"results":[{"data":{"value":[]},"statusCode":200},{"data":{},"statusCode":429}]}""", 429)]
    public void A_refused_path_is_reported_by_its_status(string raw, int expected)
        => Assert.Equal(expected, WorkIqEvidenceMapper.FailedStatus(raw));

    [Theory]
    [InlineData(FetchResult)]
    [InlineData("""{"response":"an answer","conversationId":"synthetic"}""")]
    [InlineData("not json")]
    public void A_result_without_a_failed_path_reports_none(string raw)
        => Assert.Null(WorkIqEvidenceMapper.FailedStatus(raw));
}
