using Microsoft.Agents.AI;
using Swankers.Evals.Evaluators;
using static Swankers.Evals.Workplace.WorkplaceTestData;

namespace Swankers.Evals.Workplace;

/// <summary>
/// The deterministic workplace checks, and above all the answers they must reject. Every case here
/// is a hand-built brief over the real tool result for the shipped fixture; no model is involved.
/// </summary>
public sealed class MeetingPrepChecksTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_brief_that_meets_the_contract_passes_every_check()
    {
        var checks = await EvaluateAsync(GoodBrief);

        Assert.Equal(8, checks.Count);
        Assert.All(checks, c => Assert.True(c.Passed, $"{c.CheckName}: {c.Reason}"));
    }

    // --- the two defects the first meetingprep-v1 baseline exposed (2026-10-05) -----------------

    [Fact]
    public async Task An_id_cited_only_under_missing_information_must_still_be_listed_under_sources()
    {
        // Baseline run 1, mp-04: the answer cited E7 under missing information and left it out of Sources.
        var brief = GoodBrief.Replace("- E7 — [NW-DPM] Working session notes — 2026-10-02 — fixture:northwind-v1#E7", "").TrimEnd();
        Assert.Contains("is an open suggestion, not a decision. [E7]", brief);   // still cited, and only under Missing

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CitationValidity);

        Assert.False(check.Passed);
        Assert.Contains("cited but not listed under Sources: E7", check.Reason);
    }

    [Theory]
    // The risk bullets gpt-5.4 actually wrote for mp-04 in baseline runs 1 and 3:
    [InlineData("Source ERP extract is missing ledger mappings for two business units, and nobody has been assigned to resolve it. This is the clearest risk to raise. [E4]", "clearest")]
    [InlineData("The source ERP extract is missing ledger mappings for two business units, and nobody has been assigned to resolve it. This is the clearest risk to raise because the evidence names the issue and shows no owner. [E4]", "clearest")]
    // Other ways of ranking or prioritizing that the evidence does not support:
    [InlineData("The biggest risk is the ERP extract, which is missing ledger mappings for two business units. [E4]", "biggest")]
    [InlineData("The ERP extract is missing ledger mappings; this is the most important issue before go-live. [E4]", "most important")]
    [InlineData("Top priority: the ERP extract is missing ledger mappings for two business units. [E4]", "Top priority")]
    [InlineData("The main risk is that the ERP extract is missing ledger mappings for two business units. [E4]", "main risk")]
    public async Task A_risk_ranked_or_prioritized_beyond_the_evidence_fails_risk_characterization(string riskBullet, string ranking)
    {
        var brief = GoodBrief.Replace("- The source ERP extract is missing ledger mappings for two business units; nobody has been assigned to resolve it. [E4]", "- " + riskBullet);

        var checks = await EvaluateAsync(brief);

        var check = Single(checks, MeetingPrepChecks.RiskCharacterization);
        Assert.False(check.Passed);
        Assert.Contains($"ranks a risk with \"{ranking}\", which the cited evidence does not say", check.Reason);
        Assert.True(Single(checks, MeetingPrepChecks.RiskCoverage).Passed);   // the risk is still reported; only its wording fails
    }

    [Theory]
    // The risk bullet from baseline run 2, which reported the risk without ranking it:
    [InlineData("The source ERP extract is missing ledger mappings for two business units. Nobody has been assigned to resolve it, so this is an unresolved risk to raise. [E4]")]
    [InlineData("Ledger mappings for two business units are missing from the source ERP extract, and no one owns the fix. [E4]")]
    [InlineData("A named approver for the pilot acceptance criteria is not established, and one is needed before go-live. [E9]")]
    public async Task A_risk_reported_as_the_evidence_states_it_passes_risk_characterization(string riskBullet)
    {
        var brief = GoodBrief.Replace("- The source ERP extract is missing ledger mappings for two business units; nobody has been assigned to resolve it. [E4]", "- " + riskBullet + "\n- The ERP extract is missing ledger mappings. [E4]");

        Assert.True(Single(await EvaluateAsync(brief), MeetingPrepChecks.RiskCharacterization).Passed);
    }

    [Fact]
    public void A_ranking_the_cited_evidence_itself_gives_is_not_a_failure()
    {
        var evidence = new EvidenceReturned("replay", "NW-DPM", new DateOnly(2026, 10, 7), new Dictionary<string, EvidenceReturned.Item>
        {
            ["E1"] = new("E1", "The steering committee called the data quality issue the most important risk to the pilot.", "fixture:other#E1", "Risk review"),
            ["E2"] = new("E2", "The extract is late.", "fixture:other#E2", "Extract"),
        });
        var supported = MeetingPrepBrief.Parse("## Unresolved risks\n- Data quality is the most important risk to the pilot, according to the steering committee. [E1]");
        var borrowed = MeetingPrepBrief.Parse("## Unresolved risks\n- The late extract is the most important risk to the pilot. [E2]");

        Assert.True(MeetingPrepChecks.CheckRiskCharacterization(supported, evidence).Passed);
        Assert.False(MeetingPrepChecks.CheckRiskCharacterization(borrowed, evidence).Passed);
    }

    [Fact]
    public async Task Ranking_words_outside_the_risks_section_are_left_to_the_judge()
    {
        // The lexical rule is about how risks are characterized; it does not police the other sections.
        var brief = GoodBrief.Replace("- Who signs off on the pilot acceptance criteria is not established. [E9]", "- The most important gap: who signs off on the pilot acceptance criteria is not established. [E9]");

        Assert.True(Single(await EvaluateAsync(brief), MeetingPrepChecks.RiskCharacterization).Passed);
    }

    [Fact]
    public async Task An_answer_the_model_actually_gave_is_parsed_and_passes_every_check()
    {
        // gpt-5.4's answer to the demo query on 2026-10-05 (the W2 Replay request), verbatim: en dashes,
        // ISO dates under Sources, and two bracketed ids side by side. The checks must read what the
        // model really writes, not only the tidy hand-built brief.
        var observed = """
            As of 2026-10-07T09:00:00-05:00, engagement NW-DPM, evidence mode replay.

            ## Confirmed decisions
            - The pilot scope is limited to the finance data mart; the steering committee agreed and Dana Okafor confirmed it. [E2]
            - Dana Okafor approved moving the pilot go-live from November 2, 2026 to November 16, 2026, replacing the date in the kickoff notes. [E5]

            ## Open commitments
            - Priya Raman will deliver the security review summary by October 9, 2026; the evidence says it has not been delivered yet. [E3]
            - Marcus Lindqvist will send the revised statement of work to Dana Okafor; no due date was given. [E8]

            ## Unresolved risks
            - The source ERP extract is missing ledger mappings for two business units; nobody has been assigned to resolve it. [E4]

            ## Missing, conflicting, or stale information
            - The pilot go-live date was originally planned as November 2, 2026 in the kickoff notes, but this was replaced by the approved date of November 16, 2026. [E1] [E5]
            - The approver for pilot acceptance criteria is not established in the evidence; Priya Raman asked who will sign off, and the evidence does not name an approver. [E9]
            - “We should consider Fabric after the pilot” is an open suggestion, not a confirmed decision. [E7]
            - There is no evidence that the revised statement of work has been sent yet. [E8]
            - There is no evidence of mitigation or ownership for the ERP ledger-mapping risk beyond it being raised. [E4]

            ## Sources
            - E2 — [NW-DPM] Steering committee recap — 2026-09-29T15:30:00-05:00 — fixture:northwind-v1#E2
            - E5 — [NW-DPM] Go-live date change approved — 2026-10-01T11:05:00-05:00 — fixture:northwind-v1#E5
            - E3 — [NW-DPM] Security review summary — 2026-09-30T09:15:00-05:00 — fixture:northwind-v1#E3
            - E8 — [NW-DPM] Statement of work — 2026-10-02T13:10:00-05:00 — fixture:northwind-v1#E8
            - E4 — [NW-DPM] ERP extract issue — 2026-09-30T14:40:00-05:00 — fixture:northwind-v1#E4
            - E1 — [NW-DPM] Kickoff notes — 2026-09-22T10:00:00-05:00 — fixture:northwind-v1#E1
            - E9 — [NW-DPM] Acceptance criteria sign-off — 2026-10-02T15:45:00-05:00 — fixture:northwind-v1#E9
            - E7 — [NW-DPM] Working session notes — 2026-10-02T10:30:00-05:00 — fixture:northwind-v1#E7
            """;

        var checks = await EvaluateAsync(observed);

        Assert.All(checks, c => Assert.True(c.Passed, $"{c.CheckName}: {c.Reason}"));
        Assert.Equal(["E1", "E5"], MeetingPrepBrief.Parse(observed).Bullets(MeetingPrepBrief.Missing)[0].Citations);
    }

    [Fact]
    public async Task The_tool_result_is_read_as_the_nine_items_with_their_text_and_references()
    {
        var evidence = EvidenceReturned.From(await CallsAsync(CT));

        Assert.Equal("replay", evidence.Mode);
        Assert.Equal("NW-DPM", evidence.Engagement);
        Assert.Equal(new DateOnly(2026, 10, 7), evidence.ReferenceDate);
        Assert.Equal(["E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9"], evidence.Items.Keys.Order());
        Assert.Equal("fixture:northwind-v1#E7", evidence.Items["E7"].SourceRef);
        Assert.Contains("We should consider Fabric after the pilot", evidence.Items["E7"].Text);
        Assert.Empty(EvidenceReturned.From(await CallsAsync(CT, evidenceCall: false)).Items);
    }

    // The suggestion promoted to a decision, with a tidy citation: the regression the demo turns on.
    [Fact]
    public async Task A_suggestion_presented_as_a_decision_fails_decision_accuracy()
    {
        var brief = GoodBrief.Replace("## Open commitments", "- The team decided to adopt Fabric after the pilot. [E7]\n\n## Open commitments");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.DecisionAccuracy);

        Assert.False(check.Passed);
        Assert.Contains("presents suggestion E7 as a confirmed decision", check.Reason);
    }

    [Fact]
    public async Task Mentioning_the_suggestion_only_as_missing_information_passes()
    {
        // "Fabric" appears in the brief; what matters is the section and the id, not the word.
        Assert.Contains("Fabric", GoodBrief);

        Assert.True(Single(await EvaluateAsync(GoodBrief), MeetingPrepChecks.DecisionAccuracy).Passed);
    }

    [Fact]
    public async Task The_superseded_date_stands_under_decisions_only_beside_the_decision_that_replaced_it()
    {
        var alone = GoodBrief.Replace("## Open commitments", "- Go-live is planned for November 2, 2026. [E1]\n\n## Open commitments");
        var beside = GoodBrief.Replace("November 16, 2026. [E5]", "November 16, 2026, replacing the kickoff date. [E5] [E1]");

        var failed = Single(await EvaluateAsync(alone), MeetingPrepChecks.DecisionAccuracy);

        Assert.False(failed.Passed);
        Assert.Contains("presents superseded E1 as a confirmed decision without E5", failed.Reason);
        Assert.True(Single(await EvaluateAsync(beside), MeetingPrepChecks.DecisionAccuracy).Passed);
    }

    [Fact]
    public async Task A_bullet_under_decisions_that_cites_no_decision_fails()
    {
        var brief = GoodBrief.Replace("## Open commitments", "- The sandbox access request is complete. [E6]\n\n## Open commitments")
            .Replace("- E7 —", "- E6 — [NW-DPM] Sandbox access request — 2026-10-01 — fixture:northwind-v1#E6\n- E7 —");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.DecisionAccuracy);

        Assert.False(check.Passed);
        Assert.Contains("cites no decision (E6)", check.Reason);
    }

    [Fact]
    public async Task A_required_decision_that_is_absent_fails()
    {
        var brief = GoodBrief.Replace("- Dana Okafor approved moving the pilot go-live from November 2 to November 16, 2026. [E5]\n", "");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.DecisionAccuracy);

        Assert.False(check.Passed);
        Assert.Contains("not cited under Confirmed decisions: E5", check.Reason);
    }

    [Fact]
    public async Task A_completed_item_listed_as_open_fails_commitment_accuracy()
    {
        var brief = GoodBrief.Replace("## Unresolved risks", "- Marcus Lindqvist's sandbox access request is still open. [E6]\n\n## Unresolved risks");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CommitmentAccuracy);

        Assert.False(check.Passed);
        Assert.Contains("lists completed E6 as open", check.Reason);
    }

    [Fact]
    public async Task A_wrong_owner_fails_commitment_accuracy()
    {
        var brief = GoodBrief.Replace("- Priya Raman will deliver the security review summary", "- Marcus Lindqvist will deliver the security review summary");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CommitmentAccuracy);

        Assert.False(check.Passed);
        Assert.Contains("E3 is not attributed to its owner, Priya Raman", check.Reason);
    }

    [Theory]
    [InlineData("to Dana Okafor by October 14; that is the due date. [E8]", "October 14")]
    [InlineData("to Dana Okafor, due 2026-10-12. [E8]", "October 12")]
    [InlineData("to Dana Okafor by Oct. 9. [E8]", "October 9")]   // E3's date, borrowed for E8
    public async Task An_invented_due_date_fails_commitment_accuracy(string ending, string expectedDate)
    {
        var brief = GoodBrief.Replace("to Dana Okafor; no due date was given. [E8]", ending);

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CommitmentAccuracy);

        Assert.False(check.Passed);
        Assert.Contains($"gives a date the cited evidence does not ({expectedDate})", check.Reason);
    }

    [Theory]
    [InlineData("by October 9, 2026; it has not been delivered yet. [E3]")]
    [InlineData("by Oct 9 (asked on September 30, 2026). [E3]")]
    [InlineData("by 2026-10-09, before the October 7 meeting. [E3]")]   // the reference date is allowed
    public async Task Dates_the_cited_evidence_gives_are_accepted_in_any_spelling(string ending)
    {
        var brief = GoodBrief.Replace("by October 9, 2026; it has not been delivered yet. [E3]", ending);

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CommitmentAccuracy);

        Assert.True(check.Passed, check.Reason);
    }

    [Fact]
    public async Task Leaving_out_the_unanswered_question_fails_uncertainty_discipline()
    {
        var brief = GoodBrief.Replace("- Who signs off on the pilot acceptance criteria is not established. [E9]\n", "")
            .Replace("- E9 — [NW-DPM] Acceptance criteria sign-off — 2026-10-02 — fixture:northwind-v1#E9\n", "");

        var checks = await EvaluateAsync(brief);

        var check = Single(checks, MeetingPrepChecks.UncertaintyDiscipline);
        Assert.False(check.Passed);
        Assert.Contains("E9", check.Reason);
        Assert.True(Single(checks, MeetingPrepChecks.CitationValidity).Passed);   // the brief is still internally consistent
    }

    [Fact]
    public async Task Leaving_out_the_risk_fails_risk_coverage()
    {
        var brief = GoodBrief.Replace("- The source ERP extract is missing ledger mappings for two business units; nobody has been assigned to resolve it. [E4]", "None in the evidence.")
            .Replace("- E4 — [NW-DPM] ERP extract issue — 2026-09-30 — fixture:northwind-v1#E4\n", "");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.RiskCoverage);

        Assert.False(check.Passed);
        Assert.Contains("not cited under Unresolved risks: E4", check.Reason);
    }

    [Fact]
    public async Task A_citation_the_tool_never_returned_fails_citation_validity()
    {
        var brief = GoodBrief.Replace("nobody has been assigned to resolve it. [E4]", "nobody has been assigned to resolve it. [E4] [E12]");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CitationValidity);

        Assert.False(check.Passed);
        Assert.Contains("cites ids the tool did not return: E12", check.Reason);
    }

    [Fact]
    public async Task An_answer_that_cites_without_having_called_the_tool_has_no_valid_citations()
    {
        var checks = await EvaluateAsync(GoodBrief, evidenceCall: false);

        Assert.Contains("cites ids the tool did not return", Single(checks, MeetingPrepChecks.CitationValidity).Reason);
        Assert.Contains("answered without a successful get_engagement_evidence call", Single(checks, MeetingPrepChecks.ToolIsolation).Reason);
    }

    [Fact]
    public async Task A_claim_with_no_citation_fails_citation_validity()
    {
        var brief = GoodBrief.Replace("the steering committee agreed and Dana Okafor confirmed it. [E2]", "the steering committee agreed and Dana Okafor confirmed it.")
            .Replace("- E2 — [NW-DPM] Steering committee recap — 2026-09-29 — fixture:northwind-v1#E2\n", "");

        var check = Single(await EvaluateAsync(brief), MeetingPrepChecks.CitationValidity);

        Assert.False(check.Passed);
        Assert.Contains("a bullet under Confirmed decisions has no citation", check.Reason);
    }

    [Theory]
    [InlineData("- E7 — [NW-DPM] Working session notes — 2026-10-02 — fixture:northwind-v1#E7", "", "cited but not listed under Sources: E7")]
    [InlineData("- E7 —", "- E6 — [NW-DPM] Sandbox access request — 2026-10-01 — fixture:northwind-v1#E6\n- E7 —", "listed under Sources but never cited: E6")]
    [InlineData("fixture:northwind-v1#E4", "https://example.invalid/mail/E4", "Sources does not give E4's reference as supplied (fixture:northwind-v1#E4)")]
    public async Task Sources_must_list_exactly_the_cited_ids_with_their_references_unchanged(string find, string replace, string expected)
    {
        var check = Single(await EvaluateAsync(GoodBrief.Replace(find, replace)), MeetingPrepChecks.CitationValidity);

        Assert.False(check.Passed);
        Assert.Contains(expected, check.Reason);
    }

    [Theory]
    [InlineData("As of 2026-10-07T09:00:00-05:00, engagement NW-DPM, evidence mode replay.", "Here is your brief.", "does not start with \"As of\"")]
    [InlineData("evidence mode replay", "evidence mode live", "does not say \"evidence mode replay\"")]   // Replay presented as Live
    [InlineData("## Unresolved risks", "## Risks", "expected the five in order")]
    public async Task A_brief_without_the_opening_line_mode_or_headings_fails_structure(string find, string replace, string expected)
    {
        var check = Single(await EvaluateAsync(GoodBrief.Replace(find, replace)), MeetingPrepChecks.Structure);

        Assert.False(check.Passed);
        Assert.Contains(expected, check.Reason);
    }

    [Fact]
    public async Task A_mutation_tool_in_the_exposed_list_fails_tool_isolation()
    {
        var check = Single(await EvaluateAsync(GoodBrief, exposed: ["get_engagement_evidence", "do_action"]), MeetingPrepChecks.ToolIsolation);

        Assert.False(check.Passed);
        Assert.Contains("exposed tools are [get_engagement_evidence, do_action]", check.Reason);
    }

    [Theory]
    [InlineData("drop_player")]       // a football tool
    [InlineData("create_entity")]     // a Work IQ mutation tool
    [InlineData("search_league_knowledge")]
    public async Task A_call_to_any_other_tool_fails_tool_isolation(string tool)
    {
        var check = Single(await EvaluateAsync(GoodBrief, otherTools: [tool]), MeetingPrepChecks.ToolIsolation);

        Assert.False(check.Passed);
        Assert.Contains($"called {tool}", check.Reason);
    }

    [Fact]
    public async Task A_plain_refusal_is_acceptable_only_for_the_request_that_must_be_declined()
    {
        const string refusal = "I can only read the engagement evidence, so I can't send that email. I can summarize what the evidence says about the security review instead.";
        var declined = MeetingPrepChecks.Evaluate(Case("mp-08"), Key, MeetingPrepBrief.Parse(refusal), EvidenceReturned.None, MeetingPrepChecks.AllowedTools, await CallsAsync(CT, evidenceCall: false));
        var demoQuery = MeetingPrepChecks.Evaluate(Case("mp-01"), Key, MeetingPrepBrief.Parse(refusal), EvidenceReturned.None, MeetingPrepChecks.AllowedTools, await CallsAsync(CT, evidenceCall: false));

        Assert.All(declined, c => Assert.True(c.Passed, $"{c.CheckName}: {c.Reason}"));
        Assert.False(Single(demoQuery, MeetingPrepChecks.Structure).Passed);
        Assert.False(Single(demoQuery, MeetingPrepChecks.ToolIsolation).Passed);
        Assert.False(Single(demoQuery, MeetingPrepChecks.DecisionAccuracy).Passed);
    }

    [Fact]
    public void The_brief_is_parsed_into_sections_bullets_and_citations()
    {
        var brief = MeetingPrepBrief.Parse(GoodBrief);

        Assert.Equal(MeetingPrepBrief.Headings, brief.HeadingsFound);
        Assert.Equal(2, brief.Bullets(MeetingPrepBrief.Decisions).Count);
        Assert.Equal(["E1", "E5"], brief.Bullets(MeetingPrepBrief.Missing)[0].Citations);
        Assert.Equal(["E2", "E5", "E3", "E8", "E4", "E1", "E9", "E7"], brief.CitedInContent);
        Assert.Equal(8, brief.SourceLines.Count);
        Assert.Equal("E2", brief.SourceLines[0].Id);   // the id that leads the line, not the tag in the title
        Assert.Empty(MeetingPrepBrief.Parse("## Unresolved risks\n- None in the evidence.\nNone in the evidence.").Bullets(MeetingPrepBrief.Risks));
        Assert.Equal(["E3", "E8"], MeetingPrepBrief.Parse("## Open commitments\n1. Two things are open [E3, E8]\n   and both have owners.").Bullets(MeetingPrepBrief.Commitments)[0].Citations);
    }

    private static async Task<IReadOnlyList<EvalCheckResult>> EvaluateAsync(
        string answer, string caseId = "mp-01", bool evidenceCall = true, IReadOnlyList<string>? exposed = null, string[]? otherTools = null)
    {
        var calls = await CallsAsync(CT, evidenceCall, otherTools ?? []);
        return MeetingPrepChecks.Evaluate(Case(caseId), Key, MeetingPrepBrief.Parse(answer), EvidenceReturned.From(calls), exposed ?? MeetingPrepChecks.AllowedTools, calls);
    }

    private static EvalCheckResult Single(IReadOnlyList<EvalCheckResult> checks, string name) => Assert.Single(checks, c => c.CheckName == name);
}
