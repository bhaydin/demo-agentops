# WI.NET presenter runbook: Build, Trace, Test (WorkplaceMeetingPrep)

Wednesday October 7, 2026. One scenario (`WorkplaceMeetingPrep`), one fixed body of evidence, three
prompt versions, and one evaluation that catches a one-line regression. Every command below is run
from the repository root in PowerShell 7, and every one that touches a prompt names its version.
The stage commands are in [demo/winet.ps1](winet.ps1); the raw commands they run are listed too.

Rehearsed twice from a reset state on 2026-10-06 (8.7 and 8.8 minutes end to end, timings in
section 9). The fallback recording is in `artifacts/winet/fallback-recording-20261005-210125/`
(ignored; its `README.md` says what it is).

## 1. What the audience sees

| | |
|---|---|
| Evidence E7 | `Marcus Lindqvist: We should consider Fabric after the pilot. It is worth a look once the finance data mart is live.` |
| v2 output (mp-07) | E7 appears under **## Confirmed decisions**, in a sentence that itself says Fabric was not decided |
| Evaluator | `decision_accuracy` **FAIL**: presents suggestion E7 as a confirmed decision; a bullet under Confirmed decisions cites no decision (E7) |

The point to make, in this order: the sentence hedges correctly; the section carries the business
meaning (a consultant skims headings); a human skim misses the contradiction; the deterministic check
reads placement, not keywords, and has caught it in 6 of 6 v2 runs. Do not read the whole answer
aloud. `pwsh demo/winet.ps1 show -Version v2` prints only the lines that matter.

## 2. Preflight (the morning of, from a clean tree)

```powershell
git status --porcelain                      # empty: the report says "(clean)"
dotnet restore SwankersCoach.slnx --locked-mode
dotnet build SwankersCoach.slnx -c Release --no-restore
az login                                    # DefaultAzureCredential: the account with access to the Foundry project
pwsh demo/winet.ps1 preflight
```

`preflight` checks: SDK `10.0.401`; both Release binaries; `aspire` CLI; HEAD and a clean tree; the
six frozen hashes (`meetingprep-v1.1` `5A57E9C3…`, `meetingprep-v2` `1A25C091…`, fixture
`72FE0287…`, cases `B33E5C32…`, key `40A5B8BE…`, settings `FFCCDE25…`); `az` sign-in and a token for
`https://ai.azure.com`; leftover `WORKPLACE_*` variables; ports 18888, 4318, 8088; existing workplace
state; the latest v1.1 and v2 reports. Every line must be `ok` or an understood `note`. A `FAIL`
line is fixed before anything else.

Environment variables the demo uses (nothing secret; the eval action sets and restores its own):

| Variable | Set by | Value |
|---|---|---|
| `FOUNDRY_PROJECT_ENDPOINT` | you, or the script's default | `https://foundry-swankers-vxzd.services.ai.azure.com/api/projects/swankers-coach` |
| `WORKPLACE_EVAL` | `eval` action | `1` (opt-in; without it the evaluation is skipped, and a skip is not a pass) |
| `WORKPLACE_PROMPT_VERSION` | `eval` action | `v1.1` or `v2`, always explicit; the evaluation fails without it |
| `WORKPLACE_EVAL_RUN` | `eval` action | the report label |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL` | `coach` action | `http://localhost:4318`, `http/protobuf` |
| `WORKIQ_ACCOUNT` | you, Live only | the signed-in Work IQ account; passed to the CLI, never written to a file |

Prompt versions: `v1.1` is the accepted baseline and the workplace default (`Coach:PromptVersion`
unset means v1.1 for the workplace scenario and v1 for the football Coach); `v2` is the regression;
`v1` is the superseded first prompt and is not used on stage. A version a scenario does not have
stops startup: `No instructions for prompt version 'v0' (meetingprep-v0.md). Available: v1, v1.1, v2.`

Known-good start state: `pwsh demo/winet.ps1 reset` removes `~/.agentserver/scenarios/workplace-*`
(the workplace Responses stores, one per prompt version and mode; football state is untouched) and
keeps every report. Run it once before the talk. The evaluation never reuses a session anyway (a new
conversation per case), so a stale store cannot change a result; the reset is for the Coach terminal.

## 3. Terminals

| Terminal | Command | Stays up |
|---|---|---|
| A, dashboard | `pwsh demo/winet.ps1 dashboard` (runs `aspire dashboard run --frontend-url http://localhost:18888 --otlp-http-url http://localhost:4318 --allow-anonymous`) | whole talk; stop with Ctrl+C in this terminal, never by closing the window (a closed wrapper leaves `aspire-managed` holding the ports) |
| B, Coach | `pwsh demo/winet.ps1 coach -Version v1.1` (runs `dotnet run --project src/Swankers.Coach -c Release --no-build -- --Coach:Scenario workplace --Workplace:Mode Replay --Coach:PromptVersion v1.1 --Coach:ProjectEndpoint <endpoint>` with the OTLP variables set) | the trace segment; Ctrl+C to stop |
| C, stage | every other command | whole talk |

The Coach's first log line is the identification line; read it aloud once:
`Scenario workplace, evidence mode REPLAY: agent MeetingPrep-v1-1, instructions meetingprep-v1.1 (sha256 5A57E9C3…), reference time 2026-10-07T09:00:00-05:00, fixture northwind-v1, model gpt-5.4 at …, tools get_engagement_evidence.`
The second says `response state under ~/.agentserver/scenarios/workplace-v1.1-replay; Application Insights export off; OTLP endpoint http://localhost:4318`.

## 4. The sequence (about 9 minutes of machine time; see section 9 for how to hide the waits)

1. **Trace.** With A and B up: `pwsh demo/winet.ps1 ask`. It posts the demo query to
   `http://localhost:8088/responses` with a `traceparent`, prints the answer (opening line
   `As of 2026-10-07T09:00:00-05:00, engagement NW-DPM, evidence mode replay.`) and the trace id.
   In the dashboard: Traces, resource `Swankers.Coach`, that trace id. Show four spans and skip
   the rest (HTTP, token, server spans):
   `invoke_agent MeetingPrep-v1-1` (gen_ai.agent.name, input/output tokens)
   → `chat gpt-5.4` with `gen_ai.response.finish_reasons = ["tool_calls"]`
   → `execute_tool get_engagement_evidence` ▸ `workplace.get_engagement_evidence`
     (`swankers.workplace.mode = replay`, `swankers.workplace.fixture = northwind-v1`, `swankers.workplace.items = 9`; a Live run adds a child `workiq.tool fetch` with `swankers.workplace.mode = live` and the items kept and dropped; no evidence text is ever on a span)
   → `chat gpt-5.4` with `finish_reasons = ["stop"]`.
   Typical: 17 to 25 s end to end, of which the first model call is 6 to 10 s.
2. **Baseline.** `pwsh demo/winet.ps1 eval -Version v1.1 -Run control` → `evaluation PASS`,
   64/64 deterministic, judged 16/16 (one judged miss is possible and still a pass; see section 7).
3. **Regression.** `pwsh demo/winet.ps1 eval -Version v2 -Run regression` → `evaluation FAIL`,
   `deterministic 63/64`, `mp-07 FAIL decision_accuracy: presents suggestion E7 as a confirmed decision …`.
4. **Diagnosis.** `pwsh demo/winet.ps1 show -Version v2` prints the gate, the per-case lines, the
   mp-07 bullet under `## Confirmed decisions` that cites `[E7]`, and E7 as the tool returned it.
   Then `pwsh demo/winet.ps1 diff`: the one rule v2 changed, baseline above, change below. The
   report itself (path printed) has the full answer and every verdict if someone asks. Say: same
   evidence (fixture hash in every report), same judge, same model, same cases; only the prompt
   differs, and the trace shows one `get_engagement_evidence` call per case.
5. **Recovery.** `pwsh demo/winet.ps1 eval -Version v1.1 -Run recovery` → `evaluation PASS`.
   Nothing was edited; the configuration went back to the unchanged baseline prompt.

Raw commands behind steps 2, 3, and 5, for a terminal without the script:

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://foundry-swankers-vxzd.services.ai.azure.com/api/projects/swankers-coach'
$env:WORKPLACE_EVAL = '1'; $env:WORKPLACE_PROMPT_VERSION = 'v1.1'; $env:WORKPLACE_EVAL_RUN = 'control'   # v2 / regression; v1.1 / recovery
dotnet test tests/Swankers.Evals -c Release --no-build --filter "FullyQualifiedName~MeetingPrepEvals" --logger "console;verbosity=normal"
```

Report location: `artifacts/evals/workplace/meetingprep-<version>-<run>-<UTC stamp>.md` and `.json`;
`latest-meetingprep-<version>.json` is a rolling copy. The W3 baseline and W4 regression reports are
preserved in `artifacts/evals/workplace/w3-baseline/` and `w4-regression/`. Trace location: the
dashboard at `http://localhost:18888`, Traces, resource `Swankers.Coach`; spans live only while the
dashboard runs (a decoded copy of one trace is in the fallback recording).

## 5. Live or Replay

Every surface says which: the Coach's identification line (`evidence mode REPLAY` or `LIVE`); the
answer's first line (`evidence mode replay`/`live`, taken from the tool result, never from the
prompt); the span tag `swankers.workplace.mode`; the report's provenance row `Scenario, mode`. The
evaluation is always Replay. Replay is never described as Live; if the retrieval segment is a
recording, say so.

Live retrieval (optional opening, Tier 1 in the build plan): sign in to the Work IQ CLI beforehand
(`workiq auth login`; `workiq config set disableBrokeredAuth=true` is already in place for the WAM
issue), then in terminal B `$env:WORKIQ_ACCOUNT = '<account>'; pwsh demo/winet.ps1 coach -Version v1.1 -Live`.
Startup connects to Work IQ first and fails fast if it cannot. The agent gets the same one tool;
only `fetch` is allow-listed and called, with the fixed Inbox query for the `[NW-DPM]` tag; retrieved
mail text goes to the model deployment in the maintainer's Foundry project and nowhere else, and
spans carry counts only. The go/no-go for Live on stage was set for Tuesday 12:00 Central; without a
fresh live smoke inside the preflight window, open with Replay and the recorded proof.

## 6. When something fails

| Symptom | What it is | Do |
|---|---|---|
| `preflight`: `az not signed in` or no token | Azure sign-in | `az login` in terminal C, rerun preflight |
| Coach exits with `Workplace:Mode must be Live or Replay` | mode not given | the `coach` action always passes it; if typing by hand, add `--Workplace:Mode Replay` |
| Coach exits with `Work IQ …` or the CLI's sign-in error (Live) | Work IQ auth/retrieval | `workiq auth login` and retry once; otherwise restart without `-Live` and say the retrieval segment is the recording |
| `ask` fails to connect | Coach not listening | wait for `Now listening on: http://[::]:8088` in B |
| `eval` prints nothing and no report | skipped: opt-in missing | the action sets `WORKPLACE_EVAL=1`; by hand, set it and `WORKPLACE_PROMPT_VERSION` |
| `eval` report says `INCOMPLETE` | a judge call failed or timed out | that run is incomplete, not a pass or a fail; rerun the same version once; say what happened |
| `eval` on v1.1 shows `judged 15/16` and PASS | one judge verdict on a paraphrase | expected noise (section 7); the gate holds at 80% |
| v2 passes | has not happened in 6 of 6 runs | show the W4 reports in `w4-regression/` and say so plainly; do not rerun to chase a failure |
| Dashboard ports in use | a previous dashboard left running | `taskkill /IM aspire-managed.exe /F` (or find the pid with `Get-NetTCPConnection -LocalPort 18888`) |
| Model slow (a case near the 180 s timeout) | service latency | the report marks the case; keep going, the gate reads what was graded and says incomplete if needed |

## 7. What varies, and what does not

Across 11 runs of `meetingprep-v1.1` on gpt-5.4 with judge-v2 (3 baseline, then a control and a
recovery in W4, in each rehearsal, and in the recording), every deterministic check passed every
time; the judge failed one statement in 2 of those 11 runs, both times the paraphrase "replacing the
kickoff date" for "replaces the date in the kickoff notes", always within the 80% threshold. Across 6 runs of `meetingprep-v2`,
`decision_accuracy` failed at mp-07 every time with E7 under Confirmed decisions; once (W4 run 1)
mp-06 also lost a judged check, and once (the recording) mp-07 also lost the paraphrase verdict. The
checks the demonstration depends on have never varied.

## 8. Known-good final state

Terminal B stopped (Ctrl+C), terminal A stopped (Ctrl+C). `git status --porcelain` empty.
`meetingprep-v1.1` is the workplace default and `meetingprep-v1.1.md`, `meetingprep-v2.md`, the fixture,
the cases, the key, the settings, and the judge configuration are unchanged (preflight's hash lines).
The football Coach is untouched: no scenario means football, `v1`, owner credential, as before.
Reports stay under `artifacts/evals/workplace/`.

## 9. Rehearsal timings and friction

| Step | Rehearsal 1 | Rehearsal 2 | Recording |
|---|---|---|---|
| reset + preflight | 5 s | 5 s | 5 s |
| dashboard up, Coach up | 3 s | 3 s | 4 s |
| ask (traced) | 25 s | 21 s | 32 s |
| eval v1.1 control | 152 s PASS | 176 s PASS | 182 s PASS |
| eval v2 regression | 166 s FAIL | 163 s FAIL | 168 s FAIL |
| show + diff | 1 s | 1 s | 1 s |
| eval v1.1 recovery | 167 s PASS | 157 s PASS | 163 s PASS |
| total | 8.7 min | 8.8 min | 9.3 min |

Friction found and settled: (1) each evaluation is 2.5 to 3 minutes of silence; start the
regression run at the beginning of the code walkthrough (section 10) and present the walkthrough
while it runs, or start the control run before the talk and show its report; (2) a dashboard
closed by closing the window leaves `aspire-managed` on the ports (section 6); (3) the first model
call of a Coach process pays the token acquisition (4 to 7 s inside the first `chat` span), so
send one `ask` before the audience arrives; (4) typing the prompt version by hand is where a demo
goes wrong, so every action requires `-Version`; (5) when the tree is dirty the report says
`(dirty)` next to the commit, so commit before the talk.

## 10. Code walkthrough: six stops

Keep it to the files below; the architecture is not expanded on stage.

1. **Scenario composition.** [src/Swankers.Coach/Program.cs:45-84](../src/Swankers.Coach/Program.cs#L45-L84): one process, one scenario (`CoachScenarios.Parse`), the prompt version resolved per scenario, then `WorkplaceScenario.ComposeAsync` and `CoachAgentFactory.Create`. [src/Swankers.Coach/Workplace/WorkplaceScenario.cs:32-60](../src/Swankers.Coach/Workplace/WorkplaceScenario.cs#L32-L60): instructions hashed, one tool, a Live or Replay provider; nothing from the football stack.
2. **Work IQ context-provider boundary.** [src/Swankers.Coach/Workplace/IWorkplaceContextProvider.cs:9](../src/Swankers.Coach/Workplace/IWorkplaceContextProvider.cs#L9): `GetEvidenceAsync`, `Mode`, `Fixture`. [WorkIqContextProvider.cs:40-65](../src/Swankers.Coach/Workplace/WorkIqContextProvider.cs#L40-L65) connects to the Work IQ CLI as a local MCP server (stdio) and lists its tools; [FixtureContextProvider.cs](../src/Swankers.Coach/Workplace/FixtureContextProvider.cs) is the same interface over the hashed fixture. The agent cannot tell them apart except by `Mode`.
3. **Fetch-only allow-list and evidence mapping.** [WorkplaceOptions.cs:67-89](../src/Swankers.Coach/Workplace/WorkplaceOptions.cs#L67-L89): allow-list defaults to `fetch`, read tool `fetch`, the fixed Inbox query for the tag. [WorkIqToolPolicy.cs:22](../src/Swankers.Coach/Workplace/WorkIqToolPolicy.cs#L22): mutation-shaped names never pass. [WorkIqContextProvider.cs:73-125](../src/Swankers.Coach/Workplace/WorkIqContextProvider.cs#L73-L125): the one call, errors are errors, tagged items numbered E1.. by receipt. [WorkIqEvidenceMapper.cs:65](../src/Swankers.Coach/Workplace/WorkIqEvidenceMapper.cs#L65): the Graph shape to `EvidenceItem`, with `businessDate` read from the text and `receivedAt` kept as transport metadata ([EvidenceItem.cs](../src/Swankers.Coach/Workplace/EvidenceItem.cs)).
4. **Versioned prompt selection.** [prompts/PromptLibrary.cs:24-34](../src/Swankers.Coach/prompts/PromptLibrary.cs#L24-L34): `meetingprep-<version>.md`, a missing version names the file and what exists. [CoachScenarios.cs:26-33](../src/Swankers.Coach/CoachScenarios.cs#L26-L33): the default per scenario. The two prompts side by side: [meetingprep-v1.1.md:16](../src/Swankers.Coach/prompts/meetingprep-v1.1.md#L16) against [meetingprep-v2.md:16](../src/Swankers.Coach/prompts/meetingprep-v2.md#L16); [WorkplaceScenarioTests.cs](../tests/Swankers.Coach.Tests/WorkplaceScenarioTests.cs) asserts that is the only difference.
5. **OpenTelemetry spans.** [CoachAgentFactory.cs:50](../src/Swankers.Coach/CoachAgentFactory.cs#L50): `OpenTelemetryAgent` gives `invoke_agent`, `chat`, `execute_tool`. [EngagementEvidenceTool.cs:33-44](../src/Swankers.Coach/Workplace/EngagementEvidenceTool.cs#L33-L44): the tool's own span with mode, fixture, and count, no content. [WorkIqContextProvider.cs:88-91](../src/Swankers.Coach/Workplace/WorkIqContextProvider.cs#L88-L91): `workiq.tool fetch`, metadata only. [Program.cs:136-140](../src/Swankers.Coach/Program.cs#L136-L140): the sources joined into one trace.
6. **Evaluation and the failing check.** [tests/Swankers.Evals/Workplace/MeetingPrepEvals.cs:26-83](../tests/Swankers.Evals/Workplace/MeetingPrepEvals.cs#L26-L83): opt-in, explicit version, eight cases, a new session each, provenance, the gate. [MeetingPrepChecks.cs:194](../tests/Swankers.Evals/Workplace/MeetingPrepChecks.cs#L194): `CheckDecisions` reads the section and the cited id against the answer key; a suggestion cited under Confirmed decisions fails whatever the sentence says. [workplace-golden/northwind-v1.key.json](../tests/Swankers.Evals/workplace-golden/northwind-v1.key.json): E7 is a suggestion.

## 11. Wednesday preflight checklist

- [ ] `git status --porcelain` empty; HEAD is the rehearsed commit.
- [ ] `dotnet restore --locked-mode`, Release build, `pwsh demo/winet.ps1 preflight` all `ok`.
- [ ] `az login` done; token line in preflight.
- [ ] Work IQ: `workiq auth login` done and a live smoke run inside the window, or the decision to open with Replay and the recording.
- [ ] Terminal A dashboard up; terminal B Coach up on `v1.1`; one `ask` sent and its trace seen in the dashboard.
- [ ] Control run done before the talk (or planned during the walkthrough); its report open.
- [ ] `artifacts/evals/workplace/w4-regression/` and the fallback recording folder at hand.
- [ ] Close-out plan: Ctrl+C in B and A; `v1.1` remains the default.
