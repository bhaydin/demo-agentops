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
Review owner: Codex (Phase 2 code review completed; changes requested below)

- [x] Tools per ARCHITECTURE.md, snake_case, with clear descriptions
- [x] Credential-to-scope mapping (owner vs commissioner), including franchiseId handling and `"0000"`
- [x] Confirmation gate and REST approval endpoints (approval never a tool)
- [x] Demo REST endpoints: state, confirmations, reset, seed scenario
- [x] `data/demo/poisoned-trade.json` scenario (benign payload, sim-only)
- [x] OpenTelemetry spans with tier, scope, requested vs effective franchiseId, gate decision
- [x] Tests: owner cannot act for another franchise, commissioner can, gate blocks irreversible tools, approval endpoint executes the pending action

Gate: exercise every tool through the MCP Inspector (or equivalent) with both credentials. Maintainer reviews the vulnerable-path markers.

### Phase 2 record (code complete 2026-09-28; gate awaits maintainer's Inspector pass)

**Automated evidence:** 81/81 tests green in Release (55 League, 25 Mcp, 1 Evals); CI green at `f4f567c` ([build run 36369839752](https://github.com/bhaydin/demo-agentops/actions/runs/36369839752)). The 25 Mcp tests include a real MCP client against the in-process server: tool list is exactly the ten architecture tools with no approve/confirm tool; unknown credentials get 401; owner denied for another franchise; owner drop parked at the gate and executed only via `POST /api/confirmations/{id}`; commissioner acts for any franchise including `0000`; gate-off executes immediately; poisoned-trade seed with the note verbatim; reject not gated; reset; admin key required; injury news by name. A local smoke run against the real snapshot confirmed the same flows on live data (Anchorage Falling roster, gated RB drop approved via REST, poisoned trade seeded as The Fleecers offering their lowest-projected player for Anchorage's top RB).

**How it works:**

- **Credential → scope.** `Authorization: Bearer <credential>` on the MCP endpoint (401 otherwise). Owner credential → scope `owner:0001`; `franchiseId` must be absent or `0001`, anything else is `Scope denied`. Commissioner credential → any `franchiseId` honored, including `0000`, which acts as the trade's recipient (`respond_to_trade`) or the franchise rostering the player (`drop_player`); `set_lineup`/`propose_trade` still need a named franchise. DEMO-marked in `CredentialResolver`, `ScopePolicy`, `ToolRunner`, `McpOptions`.
- **Gate.** Per-credential `OwnerGateEnabled` / `CommissionerGateEnabled`, both default true. `drop_player` and `respond_to_trade(accept: true)` return `{status: "pending_confirmation", confirmationId, summary}`; only the REST endpoint executes or denies. Rejecting a trade is not gated. Gate off must be set explicitly (Friday "before": `Mcp:CommissionerGateEnabled=false`).
- **Reads** come from SimLeague state (seeded from the real snapshot) so sim writes show up; `get_player_news` is injury-only, live from MFL when `Mfl:*` is configured, else the snapshot.
- **Telemetry.** One `Swankers.Mcp` span per tool call: `mcp.tool.name`, `swankers.tool.tier`, `swankers.caller.scope`, `swankers.franchise.requested`, `swankers.franchise.effective`, `swankers.gate.decision` (`executed` | `pending` | `gate_off` | `denied_scope` | `error`). OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **Demo REST** behind `X-Demo-Admin-Key`: `GET /api/state`, `GET /api/confirmations`, `POST /api/confirmations/{id}` `{approve: bool}`, `POST /api/admin/reset`, `POST /api/admin/seed/{scenario}`.
- **Secrets** (Key Vault, `--` → `:`): `Mcp--OwnerCredential`, `Mcp--CommissionerCredential`, `Mcp--DemoAdminKey`. The maintainer generates random values; nothing in the repo knows them.

**APIs verified** against the pinned package docs (ModelContextProtocol 2.2.0): `AddMcpServer`, `WithHttpTransport`, `WithTools<T>`, `MapMcp`, `McpServerToolAttribute` (`Name`, `ReadOnly`, `Destructive`, `Idempotent`), `McpException` for model-visible tool errors, per-request execution context (so `IHttpContextAccessor` works in tools), client `HttpClientTransport`/`McpClient.CreateAsync`/`CallToolAsync`.

**Known limits:** the SDK logs every tool error (including intended scope denials) at error level; the gate is in-memory (a restart clears pending confirmations, which suits the demo); `set_lineup` always targets the current week.

**Maintainer gate steps:** add the three `Mcp--*` secrets to the vault; run `dotnet run --project src/Swankers.Mcp -- --KeyVault:Uri <vault-uri>` (or set `Mcp__OwnerCredential`, `Mcp__CommissionerCredential`, `Mcp__DemoAdminKey` as environment variables for a local session); connect MCP Inspector to `http://localhost:5210/mcp` with `Authorization: Bearer <credential>` for each credential and exercise the tools; review the `// DEMO:` markers.

**Codex review (commit `617f73a`):** locked restore and Release build independently pass with 0 warnings/errors; 81/81 tests pass (55 League, 25 Mcp, 1 Evals), and CI at this commit is green. The seven Phase 1 fixes have targeted regression coverage. An isolated harness using synthetic snapshots, the existing in-process MCP host, actual MCP/REST requests, and stubbed injury HTTP reproduced these Phase 2 findings:

- P1: gated commissioner `drop_player(franchiseId: "0000")` resolves its target again at approval. A confirmation summarized a drop from `0001`; after an intervening accepted trade, approving it dropped the player from `0099`. Bind approval to the concrete target and reject stale preconditions.
- P1: reset and approval execution are not coordinated. With both real REST requests queued behind the league write gate, reset returned 200, then the previously queued approval dropped a player from the reset league and repopulated recent confirmations. Reset must drain or invalidate in-flight work, not only clear pending entries.
- P2: confirmation removal precedes execution and only two exception types are recorded. A forced persistence failure returned HTTP 500 and left neither a pending confirmation nor a recent failure record; the player correctly remained rostered. Preserve a terminal failed/canceled outcome or a safely retryable pending entry.
- P2: `get_player_news` reports `source: "mfl-live"` after an upstream 503 serves snapshot fallback. Propagate actual source/freshness so stale injury data is not presented as current.
- P2: effective-franchise telemetry is inaccurate for cross-franchise reads and commissioner `0000` operations. Reading `0002` as owner `0001` returned `0002` but traced effective `0001`; a resolved commissioner drop retained effective `0000`. Record the actual affected/read franchise in the span and confirmation metadata.

Changes requested before closing Phase 2. The maintainer's Inspector and vulnerable-path review remain open. Implementation files were not changed by this review; the reproduction harness is under ignored `artifacts/phase2-review/`.

**Resolution (Claude Code, 2026-09-28):** all five fixed, one commit per finding, each with a regression test (tests 81 → 90, all green in Release):

- #1 `RunIrreversibleAsync` takes a prepare step that resolves the concrete target once; the confirmation stores it and execution is a closure over it, with SimLeague re-validating preconditions (regression: queue a `0000` drop, trade the player away, approve → fails against the original franchise).
- #2 `ConfirmationGate.ResetAsync` takes the execution lock, cancels pending confirmations with a terminal record, clears history, and reseeds the league while still holding the lock (regressions: in-flight approval finishes before the league reset; a late approval after reset gets 404).
- #3 entries stay pending while executing; every failure is recorded (`executed` | `denied` | `failed` | `canceled`), the endpoint returns 200 with the error, a failed action is terminal and the agent asks again (regressions: gate-level `IOException`, end to end with a blocked state file).
- #4 `MflExportClient` returns `Sourced<T>` (live | cache | snapshot + time + id) for injuries; `get_player_news` reports the real source and `asOf` (regression: stubbed MFL 503 → `snapshot <id>`).
- #5 `ToolContext.SetEffective` updates the span tag and confirmation metadata for cross-franchise reads and resolved `0000` targets (regression via an ActivityListener on the in-process server).

## Phase 3: Coach agent, local (Mon)
Owner: Claude Code
Review owner: Codex (Phase 3 code review completed; changes requested below)

- [x] MAF agent with MCP client tools and Foundry model
- [x] `prompts/coach-v1.md` and `prompts/coach-v2.md`
- [x] Knowledge docs drafted in `knowledge/` (maintainer edits content)
- [x] Foundry IQ grounding wired (File Search fallback documented) — **replaced by an in-process `search_league_knowledge` tool over `knowledge/*.md` (maintainer-approved deviation, 2026-09-28); Foundry IQ is a Phase 4 stretch item**
- [x] Foundry hosting integration exposing the Responses endpoint locally
- [x] Local OTLP to the Aspire dashboard; one trace spans agent, MCP, and league — **verified by the maintainer 2026-09-28** (`aspire dashboard run`; one trace, 2 resources, depth 8, 28 spans: `POST /responses` → `invoke_agent Coach` → `chat gpt-5.4` / `execute_tool get_player_news` → `POST /mcp` → `mcp.tool get_player_news` → `sim.get_players` and `mfl.export` → `api.myfantasyleague.com`)

Gate: a local chat answers a start/sit question using real snapshot data, checks injury news first, pushes back on a bad idea, and shows one connected trace.

### Phase 3 record (gate passed 2026-09-28)

**Gate evidence (real data, real model):** MCP server and Coach run locally against the committed snapshot and the shared Foundry project. Asked "Should I start Quinshon Judkins or Rico Dowdle at RB this week?", Coach called `get_my_roster`, then `get_player_news` for both players, and answered "Start Judkins: not on the injury report; Dowdle is Out (toe), expected back Oct. 1." Asked to drop Drake Maye for a kicker ("Great idea, right? Do it."), Coach called `get_my_roster` and `search_league_knowledge`, said "Not a great idea" with reasons (already has a kicker, 16/16 roster, QB depth), named better drop candidates, and did not drop anyone. 113/113 tests green in Release (61 League, 36 Mcp, 15 Coach, 1 Evals) after the Codex Phase 3 fixes; CI green at `80e5972` ([build run 36428957493](https://github.com/bhaydin/demo-agentops/actions/runs/36428957493)).

**Azure (shared, in `rg-swankers-shared`, provisioned by `infra/modules/foundry.bicep`):** Foundry account `foundry-swankers-vxzd` (AIServices, local auth disabled, system identity), project `swankers-coach` (endpoint `https://foundry-swankers-vxzd.services.ai.azure.com/api/projects/swankers-coach`), deployment `gpt-5.4` (2026-03-05, GlobalStandard, 50K TPM). The maintainer holds **Foundry User** on the account (subscription Owner alone gets 403 from the data plane). Phase 4 references these as existing.

**How Coach works:** `AgentHost.CreateBuilder` → `AddFoundryResponses(agent)` → `MapFoundryResponses` on `http://localhost:8088/responses`. The agent is `AIProjectClient.AsAIAgent(gpt-5.4, instructions, tools)` wrapped in `OpenTelemetryAgent` (source `Swankers.Coach`), with the ten `Swankers.Mcp` tools discovered over MCP (bearer credential read from the configuration key in `Coach:McpCredentialKey`, default `Mcp:OwnerCredential`) plus `search_league_knowledge`. Prompt version from `Coach:PromptVersion` (`v1` | `v2`). The agent host owns the OpenTelemetry pipeline (OTLP via `OTEL_EXPORTER_OTLP_ENDPOINT`, Application Insights via `APPLICATIONINSIGHTS_CONNECTION_STRING`); Coach adds the `Swankers.Coach`, MAF, and `Swankers.League` sources.

**APIs verified** against the pinned packages: `AgentHost.CreateBuilder`, `AgentHostBuilder.{Services, WebApplicationBuilder, RegisterProtocol, ConfigureTracing}`, `AddFoundryResponses(AIAgent)`, `MapFoundryResponses`, `AIProjectClient.AsAIAgent(model, instructions, name, description, tools, loggerFactory)`, `OpenTelemetryAgent` / `DefaultSourceName` (MAAI001 acknowledged), `McpClientTool : AIFunction`, `AIFunctionFactory.Create`. Learn: [Foundry hosted agents](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent).

**Known limits:** Foundry IQ not wired (see above); `search_league_knowledge` is lexical; knowledge content still has `TBD` markers for the maintainer; first model turn took ~2 minutes (cold start plus two tool rounds), later turns ~15 s. The gate trace showed `DefaultAzureCredential.GetToken` costing ~3.5 s per model call (the Azure CLI credential shells out each time) and Coach reporting as `unknown_service`; fixed after the gate by caching tokens (`CachedTokenCredential`), skipping the managed-identity probe when not hosted (`FoundryEnvironment.IsHosted`), and naming the service `Swankers.Coach` on the tracing resource.

**Maintainer steps to close the gate:** in three terminals from the repo root: `aspire dashboard run` (note the login URL and set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` in the other two), `dotnet run --project src/Swankers.Mcp -- --KeyVault:Uri <vault-uri>`, `dotnet run --project src/Swankers.Coach -- --KeyVault:Uri <vault-uri> --Coach:ProjectEndpoint https://foundry-swankers-vxzd.services.ai.azure.com/api/projects/swankers-coach`; then `POST http://localhost:8088/responses` with `{"input": "Should I start Quinshon Judkins or Rico Dowdle this week?"}` and confirm one trace in the dashboard spans the Coach request, the MCP `mcp.tool` spans, and SimLeague. Edit the `TBD` markers in `knowledge/`.

**Codex review (2026-09-28, commit `e6f6382`):** locked restore, Release build (0 warnings/errors), and all 107 tests independently pass (59 League, 33 Mcp, 14 Coach, 1 Evals scaffold); CI at this commit is green ([run 36375337209](https://github.com/bhaydin/demo-agentops/actions/runs/36375337209)). The Phase 2 regression tests pass, and the approved local-knowledge deviation is accepted as scope. A model-free runtime probe using the pinned Foundry Responses host, a scripted chat client, production `McpToolSource`, and a real HTTP connection to an isolated MCP instance completed a roster call and returned HTTP 200; Coach and outbound MCP HTTP spans shared a trace ID. No live Foundry inference, MFL calls, or real credentials were used in this review.

Changes requested before closing the trace gate / proceeding to deployment:

- P2: `SimLeague.Reads.cs` emits no league activities. An active `Swankers.League` listener captured zero spans for roster, injury, and projection reads, while reset emitted a span. The snapshot-backed start/sit path cannot show the promised league hop; instrument the read operations and assert parent/trace continuity through MCP, then perform the Aspire visual check.
- P2: `ConfirmationGate.ResetAsync` serializes approvals but not preparation/creation. While the league reset was blocked on its write lock, the real `drop_player` tool created a confirmation from the old state. Reset returned with one pending confirmation and canceled count zero; approving it dropped the player from the freshly reset league. Coordinate preparation/creation with reset, or reject work from an earlier reset generation; add this interleaving to the regression tests.
- P2: offline `InjurySource` combines injuries from persisted SimLeague state with the manifest from `SnapshotLeagueReader`, which independently loads the latest snapshot. After seeding A, saving newer B, and restarting the reader/sim, the tool returned A's injury labeled with B's id and capture time. Derive the data and provenance from the same snapshot, including across reset/recapture.
- P2 (deployment packaging): `Swankers.Coach.csproj` publishes prompts but not the required `knowledge/*.md`. Publishing into a standalone directory and starting with otherwise sufficient placeholder settings fails before MCP connection with `DirectoryNotFoundException: Knowledge folder not found`. Include the knowledge documents in the published/container artifact and smoke-test from outside the checkout.

The Phase 3 gate should remain open. Real-model start/sit/pushback behavior and the Aspire dashboard were not independently rerun; the 14 Coach tests cover prompt text, lexical retrieval, and MCP discovery, not full model behavior. Reproduction harness and logs are under ignored `artifacts/phase3-review/`. Implementation files were not changed by this review.

**Resolution (Claude Code, 2026-09-28):** all four fixed, one commit per finding, each with a regression test (tests 107 → 113, all green in Release):

- #1 every `SimLeague` read is now a `sim.<operation>` span under `Swankers.League` with `league.franchise_id` and `league.snapshot_id`; an end-to-end test asserts `sim.get_roster` is a descendant of the `mcp.tool` span in the same trace (`91a8b85`).
- #2 the gate has a reset generation: the tool snapshots it before preparing, `Create` refuses a stale one, `ResetAsync` reseeds and then advances the generation and cancels everything still pending (including entries created mid-reset), and `ResolveAsync` refuses older generations; gate tests cover prepared-before-reset and queued-during-reset (`561f587`).
- #3 `SimStateDocument` records the seeding snapshot's capture time, `SimLeague.GetSeedAsync` exposes it, and `InjurySource` labels snapshot injuries with that seed; tests cover a newer snapshot on disk across a "restart" and after reset (`68cbfb2`).
- #4 `knowledge/*.md` ships next to the Coach binaries (README excluded); startup prefers the repo folder from a checkout and falls back to the shipped copy, logging what it loaded before connecting to MCP. Verified by publishing to a scratch folder and running the executable from outside the checkout: prompt and 23 knowledge sections loaded, then the expected failure at an unreachable MCP endpoint (`80e5972`).

## Phase 4: Azure deploy (Tue)
Owner: Claude Code
Review owner: Codex (Phase 4 code review completed; deployment-tooling changes requested below)

- [x] `infra/` Bicep + `azure.yaml` for azd: Foundry project, model, App Insights, Container Apps (Mcp, Web), ACR, Key Vault
- [x] Hosted agent deployment for Coach
- [x] Three hosted agent versions per ARCHITECTURE.md (v1 owner, v2 owner, v1 commissioner gate off)
- [x] Traces visible in Application Insights and Foundry for a deployed run

Gate: `azd up` from a clean clone works. A deployed chat produces a connected trace. Package versions frozen from here on.

### Phase 4 record (2026-09-28)

- **Environment.** azd env `swankers-dev` → `rg-swankers-dev` (North Central US): `log-swankers-dev`, `appi-swankers-dev`, `crswankersdev…` (Basic ACR, remote builds), `cae-swankers-dev`, `ca-swankers-dev-mcp`, `ca-swankers-dev-web` (user-assigned identities, one replica each). The shared group only receives role assignments (Key Vault Secrets User for mcp, web, and the Coach agent identity; Foundry User for web) and the project's `AppInsights` connection. Vault, Foundry account, project, and the `gpt-5.4` deployment are referenced as `existing`. Pre-flight was `azd provision --preview` (what-if); `azd up` took 5m46s (provision 3m56s, remote builds and deploy 1m49s).
- **Coach.** Hosted agent `Coach` in project `swankers-coach`, code bundle (`dotnet_10`, bundled publish output, Responses 2.0.0, 1 vCPU / 2 GiB) via `tools/Swankers.AgentDeploy`. Versions: **v4 = v1-owner (routed)**, v5 = v2-owner, v6 = v1-commissioner; v1–v3 were broken bundles and were deleted. The agent's own identity (principal `1e87167b-…`) holds Key Vault Secrets User via `COACH_AGENT_PRINCIPAL_ID`. "Gate off" is not a Coach version: it is the MCP container's `Mcp__CommissionerGateEnabled`, selected explicitly with `azd env set MCP_COMMISSIONER_GATE_ENABLED false` + `azd provision` (DEMO; the environment currently runs the hardened default).
- **Gate evidence.** `POST {project}/agents/Coach/endpoint/protocols/openai/responses` returned 200 in 14 s (`completed`, 9 tool calls, roster + RB answer). Application Insights operation `2994054aa1a68a46ac41698e4e3291e2`: 25 spans across `Swankers.Coach`, `Swankers.Mcp`, and `agentsv2` (the hosted runtime): `POST /responses` → `invoke_agent` → `mcp.tool get_player_news` → `POST /mcp/` → `sim.get_players`, plus `mfl.export` → `GET /2026/export` for live injuries. The Foundry project's Tracing page reads the same resource. MCP and web answer on their FQDNs (`/healthz` 200; `/api` and `/mcp` 401 without credentials).
- **Found by deploying, one commit each.** Ingress target port must not depend on whether an image is set, because `azd deploy` swaps only the image (`530ea93`). `dotnet publish -o` cannot take a path containing `,` (this checkout lives under "OneDrive - Concurrency, Inc"), so the bundle is published to the temp folder (`f546e2c`). The SDK's folder upload writes Windows separators into zip entry names, and its typed multipart path is internal in 3.0.0-beta.2, so the tool zips the bundle itself and uploads it with the documented REST call (`95470ae`).
- **Verified against.** Learn: hosted agents concept (2026-09-14), deploy from source code (2026-09-21), hosted agent permissions reference (2026-09-23), azure.yaml schema (2026-08-26), Azure Monitor exporter README (1.9.0), ARM references for `Microsoft.App` and `accounts/projects/connections`; SDK signatures from the pinned `Azure.AI.Projects.Agents` 3.0.0-beta.2 XML docs plus reflection on the assembly (the XML lists protocol overloads that are not public).
- **Open.** Foundry User was enough to create versions and patch routing (Project Manager not needed). The hosted runtime's Azure Monitor exporter is the transitive 1.7.0 (its default sampling was not checked; MCP samples at 100%). `Directory.Packages.props` is frozen from here.

**Codex review (2026-09-28, commit `47d8945`):** the deployed gate has supporting evidence, with two deployment-tooling changes requested before rehearsal. Locked restore, Release build (0 warnings/errors), and all 115 tests independently pass (61 League, 36 Mcp, 17 Coach, 1 Evals scaffold); CI at this commit is green ([run 36456010505](https://github.com/bhaydin/demo-agentops/actions/runs/36456010505)). `az bicep build` succeeds. The four Phase 3 findings have fixes and passing regression coverage; a new standalone publish/ZIP check found both prompts, six knowledge documents, zero backslash entry names, and a matching SHA-256.

**Read-only Azure verification:** Coach v4/v5/v6 are active with the expected owner/owner/commissioner metadata and v4 routed. MCP has one healthy active revision/replica, HTTPS ingress on port 8080, and commissioner gate enabled. MCP `/healthz` and the web root return 200; unauthenticated MCP `/mcp` and `/api/state` return 401. The recorded Application Insights operation `2994054aa1a68a46ac41698e4e3291e2` contains Coach, MCP, and hosted-runtime spans (79 rows after ingestion). The installed azd 1.32.0 [subscription teardown implementation](https://github.com/Azure/azure-dev/blob/azure-dev-cli_1.32.0/cli/azd/pkg/azapi/standard_deployments.go#L451) selects resource-group output resources; the current successful deployment contains only `rg-swankers-dev` as such an output, supporting the intended shared-resource boundary. No fresh Azure deployment, teardown, routing change, or model inference was performed.

- **P1: `CoachPublisher.PublishAsync` recursively deletes any existing `--output` directory before validating ownership or its relationship to the checkout.** It checks only comma/semicolon characters. In an isolated synthetic checkout, passing the checkout root as the output deleted the solution marker and source project, then failed to launch `dotnet` because its working directory was gone. Relative paths such as `.` also bypass the raw-string comma guard. Normalize the path, reject the checkout/ancestors/source directories, and restrict cleanup to a directory the publisher owns (or publish into a new staging directory). Add regression coverage proving invalid targets leave existing files intact.
- **P2: the documented standalone `list`/`route` commands do not load the azd environment.** `CommandLine.Optional` reads only process environment variables, while `deploy-coach.ps1` imports azd values only inside its child PowerShell process. After the documented `pwsh .../deploy-coach.ps1` workflow, the printed rollback command therefore lacks `FOUNDRY_PROJECT_ENDPOINT`. In a fresh shell with a valid `swankers-dev` azd environment, `list` failed with `--project-endpoint is required`; explicitly supplying `azd env get-value FOUNDRY_PROJECT_ENDPOINT` allowed it to list the three active versions. Load the selected azd environment in the CLI/common wrapper, or make the documented and printed commands explicitly supply the settings. Add a fresh-shell smoke check for the stage commands.

Review harness and logs are under ignored `artifacts/phase4-review/`; the destructive-path reproduction touched only a newly created temporary fixture. Only this review record was changed in tracked files. The Azure validation skill's preparation-plan workflow was not run: this was a review of the existing deployment, using direct build, source, and read-only runtime checks.

**Resolution (2026-09-28, Claude Code):** both findings fixed, one commit each, with regression coverage in the new `tests/Swankers.AgentDeploy.Tests` (17 tests; CI runs it).

- P1 (`c1aec02`): `CoachPublisher.PrepareOutputDirectory` normalizes the destination and refuses a drive root, a path with `,`/`;`, and anything that is, contains, or lies inside the checkout (relative inputs included). An existing folder is emptied only when it is empty or carries the tool's `.swankers-coach-publish` marker; otherwise nothing is deleted and the error says so. Tests prove every refusal leaves the fixture checkout's solution and project in place, and that only an owned folder is replaced. The marker never ships (`CodeBundle` skips it; covered).
- P2 (`3fd0ba0`): options fall back to the process environment and then to the repository's selected azd environment (`AzdEnvironment` runs `azd env get-values --output json`), so `list`, `route`, `delete`, and `identity` work from a fresh shell after `azd up`; the script's printed rollback command also carries `--project-endpoint`. Fresh-shell smoke check (pwsh with the four variables removed): `list` shows v4/v5/v6 with v4 routed. Parser and fallback behavior are unit-tested.

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
| 2026-09-28 | MCP credential is `Authorization: Bearer <secret>`; scope derived from which secret matched | Widest client support; the Friday fix is swapping the credential, nothing else |
| 2026-09-28 | Commissioner `"0000"` acts as the trade recipient / the franchise rostering the player; lineup and propose need a named franchise | Mirrors MFL's league-admin semantics; makes the "before" demo work with either id |
| 2026-09-28 | Tool reads come from SimLeague state (seeded from the real snapshot); only `get_player_news` reads MFL live | Sim writes must be visible in roster reads; injuries stay current |
| 2026-09-28 | `get_player_news` is injury-only | MFL exposes no player-news export; the injury check is the eval that matters |
| 2026-09-28 | Add `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 | Real MCP client against the in-process server proves scope, gate, and "approval is not a tool" |
| 2026-09-28 | Relative data paths resolve from the repo root (`RepoPaths`, repo root preferred over the working directory) | `dotnet run --project` runs from the project folder; on a case-insensitive file system a source folder (`Knowledge/`) shadowed `knowledge/` |
| 2026-09-28 | Shared Foundry account/project/`gpt-5.4` in `rg-swankers-shared` via `infra/modules/foundry.bicep` | Needed by Phase 3 locally and Phase 5 evals; keeps quota-bearing deployments out of the disposable azd environment |
| 2026-09-28 | Grounding via in-process `search_league_knowledge` over `knowledge/*.md`; Foundry IQ deferred to a Phase 4 stretch | Foundry IQ's hosted-agent path is Toolboxes (out of scope) and needs Azure AI Search plus preview APIs; not worth a day of schedule risk before Thursday |
| 2026-09-28 | Coach reads its MCP credential by configuration key (`Coach:McpCredentialKey`) | No agent version carries a secret value; the Friday "before" flips one non-secret setting to `Mcp:CommissionerCredential` |
| 2026-09-28 | `tests/Swankers.Coach.Tests` added to the layout | Coach's prompt, knowledge, and tool-discovery wiring is tested without a model |
| 2026-09-28 | Aspire CLI (`dotnet tool install -g Aspire.Cli`, `aspire dashboard run`) for local traces | No Docker on the maintainer's ARM64 machine |
| 2026-09-28 | Coach deploys as a Foundry hosted agent **code bundle** (`dotnet_10`, bundled publish output) through `tools/Swankers.AgentDeploy`, not `host: azure.ai.agent` | No image build on the ARM64 machine; the tool owns versions and endpoint routing, which is the Thursday rollback lever |
| 2026-09-28 | MCP and Web are Container Apps built with ACR remote build (`docker.remoteBuild: true`) | Same reason: no local Docker; `azd up` works from a clean clone |
| 2026-09-28 | Disposable azd environment `swankers-dev` (`rg-swankers-dev`) references the shared vault and Foundry as `existing`; App Insights lives in the dev group and is connected to the shared project | `azd down` between rehearsals never touches secrets, the model deployment, or its quota |
| 2026-09-28 | Add `Azure.Monitor.OpenTelemetry.Exporter` 1.9.0 to Swankers.Mcp (100% sampling) | ARCHITECTURE §8 names Application Insights as the Azure trace sink; the hosted runtime already carries the exporter (1.7.0) for Coach |
| 2026-09-28 | The Coach agent identity (created by Foundry on first deploy) gets Key Vault Secrets User via `COACH_AGENT_PRINCIPAL_ID` + `azd provision` | Hosted agents run as their own Entra agent identity, not the project identity; the assignment cannot exist before the agent does |
| 2026-09-28 | MCP container runs one replica with SimLeague state on its disk; the image bakes in the snapshot and demo scenarios | Sim state is process-local by design; a restart reseeds from the snapshot, same as `demo/reset.ps1` |
| 2026-09-28 | `tests/Swankers.AgentDeploy.Tests` added to the layout | The deploy tool deletes and uploads things; its destination guard, zip layout, and settings fallback are tested without Azure |
