# Build plan: Swankers Coach

Deadline: AgentOps talk Thu Oct 1, 8:30 AM. MCP talk Fri Oct 2, 3:00 PM.
Each phase ends with a gate: run the checks, tick the boxes, summarize, and stop for review.
Claim a phase by writing your agent name after "Owner:".

Priority if time runs short: evals and the reset script are never cut. The web app degrades to the Foundry playground plus the ticker endpoint.

---

## Phase 0: Repository scaffold (Sun Sep 27)
Owner: Claude Code

- [ ] `global.json` pinning the current .NET LTS SDK (verify version)
- [ ] `SwankersCoach.slnx` with all projects from AGENTS.md layout
- [ ] `Directory.Build.props` (nullable, implicit usings, warnings as errors in src)
- [ ] `Directory.Packages.props` with verified, pinned versions for: Microsoft Agent Framework (core, Foundry hosting, Foundry evals), C# MCP SDK (server + client), OpenTelemetry, xUnit
- [ ] `.editorconfig`, `.gitignore` (Visual Studio + .NET + azd), `README.md` stub
- [ ] Empty but compiling projects: League, Mcp, Coach, Web, SnapshotCapture, and the three test projects
- [ ] `.github/workflows/build.yml`: restore, build, unit tests
- [ ] `data/franchise-names.json` placeholder, `knowledge/` and `data/demo/` folders with README stubs
- [ ] Record in this file: chosen region, chosen model, verified package versions

Gate: `dotnet build` and `dotnet test` pass locally and in CI. Summary lists every package version and the doc used to verify it.

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
