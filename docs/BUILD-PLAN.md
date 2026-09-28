# Build plan: Swankers Coach

Deadline: AgentOps talk Thu Oct 1, 8:30 AM. MCP talk Fri Oct 2, 3:00 PM.
Each phase ends with a gate: run the checks, tick the boxes, summarize, and stop for review.
Claim a phase by writing your agent name after "Owner:".

Priority if time runs short: evals and the reset script are never cut. The web app degrades to the Foundry playground plus the ticker endpoint.

---

## Phase 0: Repository scaffold (Sun Sep 27)
Owner: Claude Code
Review owner: Codex (Phase 0 gate review completed; see review notes below)

- [x] `global.json` pinning the current .NET LTS SDK (verify version)
- [x] `SwankersCoach.slnx` with all projects from AGENTS.md layout
- [x] `Directory.Build.props` (nullable, implicit usings, warnings as errors in src)
- [x] `Directory.Packages.props` with verified, pinned versions for: Microsoft Agent Framework (core, Foundry hosting, Foundry evals), C# MCP SDK (server + client), OpenTelemetry, xUnit
- [x] `.editorconfig`, `.gitignore` (Visual Studio + .NET + azd), `README.md` stub
- [x] Empty but compiling projects: League, Mcp, Coach, Web, SnapshotCapture, and the three test projects
- [x] `.github/workflows/build.yml`: restore, build, unit tests
- [x] `data/franchise-names.json` placeholder, `knowledge/` and `data/demo/` folders with README stubs
- [x] Record in this file: chosen region, chosen model, verified package versions

Gate: `dotnet build` and `dotnet test` pass locally and in CI. Summary lists every package version and the doc used to verify it.

### Phase 0 record (verified 2026-09-27)

**Gate status:** passed. Local build (Debug and Release, 0 warnings) and `dotnet test` (3/3) pass on SDK 10.0.401, including `dotnet restore --locked-mode`. CI green on the first push: [build run 36331443534](https://github.com/bhaydin/demo-agentops/actions/runs/36331443534).

**Region:** North Central US. It supports Foundry hosted agents ([hosted agents: region availability](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents)) and cloud AI red teaming, which is only in East US 2 and North Central US ([evaluation region support](https://learn.microsoft.com/en-us/azure/foundry/concepts/evaluation-regions-limits-virtual-network)). Sweden Central is ruled out for red teaming.

**Model:** `gpt-5.4` (2026-03-05), Global Standard, one deployment for agent and judge. GA and available in North Central US ([region availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)). Fallback: `gpt-5.4-mini`. Maintainer confirms quota.

**SDK:** .NET 10.0.401 (LTS, runtime 10.0.12, released 2026-09-08), `rollForward: latestPatch`. Requires Visual Studio 18.9.3 or later ([.NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)).

**Packages** (exact pins in `Directory.Packages.props`; resolved versions confirmed in `packages.lock.json`):

| Package | Version | Stable? | Verified against |
|---|---|---|---|
| Microsoft.Agents.AI | 1.22.0 | Stable | nuget.org |
| Microsoft.Agents.AI.Foundry.Hosting | 1.22.0-preview.260918.1 | **Prerelease** (no stable exists) | [Learn: Foundry hosted agents](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent); `AddFoundryResponses` / `MapFoundryResponses` confirmed in package XML docs |
| Microsoft.Agents.AI.Foundry | 1.22.0-preview.260918.1 | **Prerelease** (Hosting requires it) | [Learn: Evaluation](https://learn.microsoft.com/en-us/agent-framework/agents/evaluation); `FoundryEvals` confirmed in package XML docs |
| Azure.AI.Projects | 3.0.0-beta.2 | **Prerelease** | Required by the Learn hosting sample; matches MAF 1.22's dependency |
| Azure.Identity | 1.21.0 | Stable | Learn hosting sample |
| ModelContextProtocol | 2.2.0 | Stable | nuget.org; `McpClient`, `HttpClientTransport` confirmed in package |
| ModelContextProtocol.AspNetCore | 2.2.0 | Stable | nuget.org; `WithHttpTransport`, `MapMcp` confirmed in package; [Learn MCP quickstart](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server) |
| OpenTelemetry.Extensions.Hosting | 1.19.1 | Stable | [Learn: OTel in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) |
| OpenTelemetry.Instrumentation.AspNetCore | 1.19.0 | Stable | same |
| OpenTelemetry.Instrumentation.Http | 1.19.0 | Stable | same |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.19.1 | Stable | same |
| xunit.v3.mtp-off | 4.0.1 | Stable | [xUnit 4.0.0 release notes](https://xunit.net/releases/v3/4.0.0) |
| xunit.runner.visualstudio | 4.0.0 | Stable | same |
| Microsoft.NET.Test.Sdk | 18.10.1 | Stable | nuget.org |

Transitive prereleases pinned by the lock files: Azure.AI.AgentServer.Core 1.0.0-beta.29, Azure.AI.AgentServer.Responses 1.0.0-beta.8.

**Known doc drift:** the Learn evaluation page shows `new FoundryEvals(chatConfiguration, ...)` in `Microsoft.Agents.AI.AzureAI`. The shipped package has `Microsoft.Agents.AI.Foundry.FoundryEvals(AIProjectClient, string model, ...)`. Phase 5 codes against the package.

**Codex review (2026-09-27, commit `1fd81f2`):** locked restore, Release build (0 warnings/errors), and all three scaffold tests independently passed. CI at that commit is also green ([run 36331497889](https://github.com/bhaydin/demo-agentops/actions/runs/36331497889)). One local-startup finding: Mcp and Coach both default to `http://localhost:5000`; running them together reproduces an address-in-use failure. Add distinct development launch profiles or document explicit ports before Phase 3 integration. This does not block Phase 1. Current tests prove runner discovery only; live Azure quota, agent/judge compatibility, and application behavior remain unverified at this scaffold gate.

## Phase 1: League gateway (Sun–Mon)
Owner: Claude Code
Review owner: Codex (Phase 1 code review completed; changes requested below)

- [x] Domain models (Franchise, Player, Roster, Lineup, Trade, Transaction, Matchup)
- [x] `ILeagueReader`, `ILeagueWriter`
- [x] `MflExportClient`: export-only, API-key auth, User-Agent, 1s spacing, response cache, snapshot fallback
- [x] `SimLeague`: seed from snapshot, apply writes, transaction log, `ResetAsync`
- [x] `Swankers.SnapshotCapture`: pulls exports into `data/snapshot/<date>/`, anonymizes owner names, applies `franchise-names.json`, adds The Fleecers as a sim-only franchise
- [x] Tests: no-import guard (no request URL may contain an import command), snapshot round-trip, SimLeague writes, reset restores exact state

Gate: capture a real Swankers snapshot (maintainer runs it with the key), seed SimLeague, all tests green.

### Phase 1 record (gate passed 2026-09-28)

**Gate evidence:** the maintainer captured `data/snapshot/2026-09-28` (12 franchises + The Fleecers, 2,657 players, weeks 1–2 complete with per-player weekly results, week 3 in progress). `RealSnapshotTests` run against that committed snapshot: internal consistency, every name from `franchise-names.json`, no emails/phones/owner text in any file, SimLeague seeds from it and `ResetAsync` restores byte-identical state. 44/44 tests green locally (42 League incl. 4 real-snapshot, 1 Mcp, 1 Evals); CI green with the real-snapshot tests running: [build run 36365947293](https://github.com/bhaydin/demo-agentops/actions/runs/36365947293).

**Verified against MFL (docs + live data):**

- Export URL shape `https://{host}/{year}/export?TYPE=…&L=…&APIKEY=…&JSON=1`; import is `/import`, which `ExportOnlyHandler` refuses at send time and `MflRequestBuilder` cannot express. Source: [MFL API docs](https://api.myfantasyleague.com/2026/api_info) and [request types](https://api.myfantasyleague.com/2026/api_info?STATE=details).
- `APIKEY` is owner-scoped and export-only; the registered User-Agent goes on every request; MFL asks for 1 s spacing, caching, and no retry (429 = throttled).
- Type names used: `league` (franchise ids only), `players`, `rosters`, `injuries`, `schedule` (matchups incl. past scores), `projectedScores`, `weeklyResults`, `leagueStandings`, `transactions`, `pendingTrades`.
- JSON quirks handled: every value is a string; empty collections are `{}`; single-item collections can be an object; transaction text is `added,ids,|dropped,ids,`.
- **Privacy finding:** the `league` export returns owner name, email, phone, and address for every franchise. Snapshots therefore store only domain records (allow-list locked by test), `MflExportClient` never logs URLs or bodies, and franchise names resolve only through `FranchiseNameMap`.

**Deviations from ARCHITECTURE.md (maintainer approved):**

- Pending trades are **not** captured into snapshots: real notes could identify members. Live `GetPendingTradesAsync` still works; demo offers come from `data/demo` scenarios (Phase 2).
- `WeeklyResult`/`PlayerResult` added to the domain and `GetWeeklyResultsAsync` to `ILeagueReader` (the architecture's reader list names weekly results; Phase 5 start/sit evals need actual per-player points).
- MFL has no player-news export. `injuries` (status, details, expected return) is the only news-like source; Phase 2's `get_player_news` needs a decision on scope.

**Known limits:** waiver-processing and IR transactions carry no player ids in MFL's text and surface as `Unknown` with the MFL label; The Fleecers' roster is redrafted from free agents on every capture; parsers for `schedule`, `leagueStandings`, `projectedScores`, and `weeklyResults` are validated against this league's real responses, not the full MFL schema.

**Re-capture** after Monday Night Football (Tue) so week 3 is final for Thursday: `dotnet run --project tools/Swankers.SnapshotCapture -- --KeyVault:Uri <vault-uri> --Capture:Week 4` from the repo root.

**Codex review (commit `b173381`):** locked restore, Release build (0 warnings/errors), and all 44 tests independently pass; CI at this commit is green. The Phase 0 port collision is fixed. Changes requested before building Phase 2 on the gateway, based on an isolated synthetic-data harness (no live MFL exports or credentials used):

- P1: concurrent first reads initialize/persist SimLeague without the write gate; 27 of 32 reads failed with `IOException`.
- P1: SimLeague publishes `_state` before persistence succeeds; a failed drop removed the player in memory while the persisted state still retained the player.
- P2: SnapshotStore publishes the manifest before the collections and treats missing files as empty; an interrupted save became the latest snapshot with zero franchises/players.
- P2: capture only warns about missing essential data; a synthetic HTTP 200 error payload was cached as empty data, bypassed fallback, and produced a successful snapshot containing only The Fleecers.
- P2: a self-trade is accepted and duplicates roster slots (2 players became 4 slots); reject identical source/target franchises.
- P2: dropping/trading away a starter leaves that player in the declared lineup; keep affected active/future lineups consistent with roster changes.
- P2: rate limiting is per MflExportClient instance, but DI creates transient clients; two factory-created clients sent requests 2.3 ms apart with a configured 1 s gap.

Add targeted regressions for these cases. Implementation files were not changed by this review.

**Resolution (Claude Code, 2026-09-28):** all seven fixed, one commit per finding, each with a regression test (League tests 44 → 57, all green in Release):

- #1 first use initializes under the write gate; loaded reads stay lock-free on the volatile immutable document.
- #2 `PersistAsync` takes the candidate document; `_state` is published only after the save succeeds (writes and reset).
- #3 `SnapshotStore` writes to `<id>.tmp` with the manifest last and publishes by directory rename; discovery and load accept only complete directories; same-id recapture retires the old snapshot whole.
- #4 MFL's HTTP 200 `{"error": …}` envelope raises `MflResponseException` (never cached, message carries no MFL text) and takes the snapshot fallback; `CaptureAsync` aborts before saving if franchises, players, rosters, or standings are empty.
- #5 self-trades are rejected at proposal.
- #6 drops and accepted trades remove moved players from the franchise's lineups for the current week onward; past weeks are preserved.
- #7 request spacing lives in a singleton `MflRateLimiter` shared by every DI-created client; the regression resolves two clients from the real registration.

## Phase 2: MCP server (Mon)
Owner: Claude Code

- [ ] Tools per ARCHITECTURE.md, snake_case, with clear descriptions
- [ ] Credential-to-scope mapping (owner vs commissioner), including franchiseId handling and `"0000"`
- [ ] Confirmation gate and REST approval endpoints (approval never a tool)
- [ ] Demo REST endpoints: state, confirmations, reset, seed scenario
- [ ] `data/demo/poisoned-trade.json` scenario (benign payload, sim-only)
- [ ] OpenTelemetry spans with tier, scope, requested vs effective franchiseId, gate decision
- [ ] Tests: owner cannot act for another franchise, commissioner can, gate blocks irreversible tools, approval endpoint executes the pending action

Gate: exercise every tool through the MCP Inspector (or equivalent) with both credentials. Maintainer reviews the vulnerable-path markers.

## Phase 3: Coach agent, local (Mon)
Owner:

- [ ] MAF agent with MCP client tools and Foundry model
- [ ] `prompts/coach-v1.md` and `prompts/coach-v2.md`
- [ ] Knowledge docs drafted in `knowledge/` (maintainer edits content)
- [ ] Foundry IQ grounding wired (File Search fallback documented)
- [ ] Foundry hosting integration exposing the Responses endpoint locally
- [ ] Local OTLP to the Aspire dashboard; one trace spans agent, MCP, and league

Gate: a local chat answers a start/sit question using real snapshot data, checks injury news first, pushes back on a bad idea, and shows one connected trace.

## Phase 4: Azure deploy (Tue)
Owner:

- [ ] `infra/` Bicep + `azure.yaml` for azd: Foundry project, model, App Insights, Container Apps (Mcp, Web), ACR, Key Vault
- [ ] Hosted agent deployment for Coach
- [ ] Three hosted agent versions per ARCHITECTURE.md (v1 owner, v2 owner, v1 commissioner gate off)
- [ ] Traces visible in Application Insights and Foundry for a deployed run

Gate: `azd up` from a clean clone works. A deployed chat produces a connected trace. Package versions frozen from here on.

## Phase 5: Evals and CI gate (Tue)
Owner:

- [ ] Golden set (15–20 cases, four categories) from completed weeks
- [ ] FoundryEvals wiring, `PushbackEvaluator`, `ToolSequenceEvaluator`
- [ ] `evalsettings.json` thresholds
- [ ] `.github/workflows/evals.yml` gates promotion of a new agent version
- [ ] Demonstrate: v1 passes, v2 fails on injury-check cases

Gate: CI shows v1 green and v2 red for the right reason.

## Phase 6: Web app (Tue)
Owner:

- [ ] Chat pane against the hosted agent
- [ ] League ticker (2s polling of `/api/state`), Brian's franchise highlighted
- [ ] Approval dialog for pending confirmations
- [ ] Header showing agent version and credential scope
- [ ] Deployed to Container Apps

Gate: the Friday "before" attack is visible in the ticker; the "after" attack surfaces an approval dialog instead.

## Phase 7: Demo hardening (Wed)
Owner: maintainer, with agent support

- [ ] `demo/reset.ps1` resets SimLeague and reseeds scenarios in under 10 seconds
- [ ] Portal red-team run: before and after attack success rate captured (maintainer)
- [ ] Backup recordings of every live demo (maintainer)
- [ ] `demo/runbook.md`: exact click path and fallback for each demo, both talks
- [ ] Full timed rehearsal of both talks with resets between

Gate: two clean rehearsals in a row.

---

## Maintainer-only tasks

- Register an MFL API client and User-Agent; obtain the export-only API key
- Confirm Swankers league members are fine with appearing on stage, and finalize `franchise-names.json`
- Azure subscription, region, and quota check
- Run the portal red-team scans and record demos

## Decisions log

| Date | Decision | Reason |
|---|---|---|
| 2026-09-27 | C# end to end | Maintainer's language; evals and red teaming reachable without Python |
| 2026-09-27 | Real reads, simulated writes | Not fake, and no stage mistake can touch the real league |
| 2026-09-27 | Scope derived from credential | Mirrors MFL commissioner behavior; makes Friday's fix an identity change |
| 2026-09-27 | Approval via REST, never a tool | The agent cannot approve its own irreversible actions |
| 2026-09-27 | Region: North Central US | Only candidate with both hosted agents and cloud AI red teaming |
| 2026-09-27 | Model: gpt-5.4, Global Standard | GA in North Central US, no quota-request tier; one deployment for agent and judge |
| 2026-09-27 | .NET SDK 10.0.401, VS 18.10 | Current LTS patch; 10.0.300 was four security releases behind |
| 2026-09-27 | xUnit v3 in VSTest mode (`xunit.v3.mtp-off`) | Keeps `dotnet test tests/<project>` working as documented; MTP mode needs `--project` |
| 2026-09-27 | NuGet lock files, locked restore in CI | Freezes transitive prereleases (Azure.AI.AgentServer.*) as well as direct pins |
| 2026-09-27 | Repo initialized in the existing OneDrive folder, remote `bhaydin/demo-agentops` | Watch for OneDrive sync conflicts in `.git` and `bin`/`obj`; move out of OneDrive if they appear |
| 2026-09-27 | Secrets in a standalone Key Vault (`kv-swankers-vxzd`, RG `rg-swankers-shared`), not the azd environment | Survives `azd down`/`up` during rehearsals; agents reference secret names only. MFL secrets verified working |
| 2026-09-27 | Snapshots store normalized, allow-listed domain data; raw MFL responses never written to disk or logged | MFL `league` export includes owner PII (name, email, phone, address) for every franchise |
| 2026-09-27 | The Fleecers = franchise `0099`, roster drawn from free agents at capture time | Sim-only villain per AGENTS.md; ID cannot collide with the 12 real franchises (`0001`–`0012`) |
| 2026-09-27 | Add `Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.2 | Apps load `Mfl:*` config from Key Vault via `--`→`:` name mapping; stable, verified on Learn |
| 2026-09-28 | Pending trades never captured into snapshots | Real trade notes could identify league members; demo offers are seeded scenarios |
| 2026-09-28 | Weekly per-player results added to the domain and snapshots | Phase 5 scores start/sit calls against completed weeks' actual points |
| 2026-09-28 | The Fleecers draft 2 QB / 5 RB / 5 WR / 2 TE / 1 PK / 1 Def from free agents | Matches the measured Swankers roster shape; deterministic per capture |
| 2026-09-28 | `Microsoft.Extensions.*` 10.0.12 pinned for class libraries | League and the capture tool need Http, Caching.Memory, Options, Logging.Abstractions, Hosting outside the ASP.NET shared framework |
