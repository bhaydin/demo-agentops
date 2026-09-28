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
Owner:

- [ ] Domain models (Franchise, Player, Roster, Lineup, Trade, Transaction, Matchup)
- [ ] `ILeagueReader`, `ILeagueWriter`
- [ ] `MflExportClient`: export-only, API-key auth, User-Agent, 1s spacing, response cache, snapshot fallback
- [ ] `SimLeague`: seed from snapshot, apply writes, transaction log, `ResetAsync`
- [ ] `Swankers.SnapshotCapture`: pulls exports into `data/snapshot/<date>/`, anonymizes owner names, applies `franchise-names.json`, adds The Fleecers as a sim-only franchise
- [ ] Tests: no-import guard (no request URL may contain an import command), snapshot round-trip, SimLeague writes, reset restores exact state

Gate: capture a real Swankers snapshot (maintainer runs it with the key), seed SimLeague, all tests green.

## Phase 2: MCP server (Mon)
Owner:

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
