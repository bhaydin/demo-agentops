# Architecture: Swankers Coach (locked)

Status: locked for Cloud & AI Summit 2026. Changes require maintainer approval (see AGENTS.md).

## Overview

```
Swankers.Web (Blazor)
   | chat (Responses protocol)          | league state + approvals (REST)
   v                                    v
Swankers.Coach (MAF agent, Foundry hosted) --MCP (streamable HTTP)--> Swankers.Mcp
   | grounding                                                          |
   v                                                                    v
Foundry IQ (knowledge/*.md)                              Swankers.League
                                                          |- MflExportClient (live reads, cached)
                                                          |- SimLeague (all writes, resettable)

OpenTelemetry on every hop -> Application Insights + Foundry tracing
Swankers.Evals (golden set, FoundryEvals, custom evaluators) -> CI gate
```

## Components

### 1. Swankers.League (class library)

One interface, two implementations.

- `ILeagueReader`: rosters, players, injuries, weekly results, projections, standings, transactions.
- `MflExportClient : ILeagueReader`: calls MFL `export` requests only, authenticated with the export-only API key. Registers a User-Agent, spaces requests about one second apart, and caches responses. Falls back to the latest snapshot in `data/snapshot/` when the network fails.
- `SimLeague : ILeagueReader, ILeagueWriter`: JSON-backed league state seeded from a snapshot. Applies lineup changes, trade proposals, trade responses, and drops. Emits a transaction log the web ticker reads.
- `ILeagueWriter` is implemented only by `SimLeague`.
- Reset: `SimLeague.ResetAsync(snapshotId)` restores the seeded state.

Verify MFL request type names against the MFL developer docs (`api_info`) before implementing. Expected types include league, rosters, players, injuries, weeklyResults, projectedScores, transactions.

### 2. Swankers.Mcp (ASP.NET Core)

C# MCP SDK, streamable HTTP transport, hosted in Azure Container Apps.

**Tools**

| Tier | Tool | Notes |
|---|---|---|
| Read | `get_my_roster` | Caller's franchise |
| Read | `get_roster(franchiseId)` | Any franchise, read-only |
| Read | `get_player_news(player)` | Injury status + news |
| Read | `get_matchup(week)` | Caller's matchup + projections |
| Read | `get_standings()` | |
| Read | `get_trade_offers()` | Pending offers incl. note text (injection vector) |
| Write | `set_lineup(starters, franchiseId?)` | |
| Write | `propose_trade(toFranchiseId, give, get, note, franchiseId?)` | |
| Irreversible | `drop_player(playerId, franchiseId?)` | Gated |
| Irreversible | `respond_to_trade(tradeId, accept, franchiseId?)` | Accept is gated |

**Scope is derived from the credential, not from a flag.** Mirrors MFL, where a commissioner's session can act for any franchise.

- Owner credential (the agent's own identity): `franchiseId` is ignored or rejected unless it equals the owner's franchise. Default.
- Commissioner credential (Brian's identity): `franchiseId` is honored for any franchise, including `"0000"`. DEMO: intentionally vulnerable.

**Confirmation gate**

- When the gate is on, irreversible tools do not execute. They create a pending confirmation and return `{ status: "pending_confirmation", confirmationId, summary }`.
- Approval happens only through the REST endpoint `POST /api/confirmations/{id}` called by the web app. **Approval is never exposed as an MCP tool.** The agent cannot approve its own actions.
- Gate mode (on/off) is set per credential in configuration. Default on.

**Demo REST endpoints** (protected by a demo admin key, not exposed as tools)

- `GET /api/state`: rosters, transaction log, pending confirmations (for the ticker)
- `GET /api/confirmations`, `POST /api/confirmations/{id}` (approve or deny)
- `POST /api/admin/reset`: restore SimLeague to snapshot
- `POST /api/admin/seed/{scenario}`: load a demo scenario (for example the poisoned trade)

**Telemetry**: a span per tool call with attributes for tool name, tier, caller scope, requested franchiseId, effective franchiseId, and gate decision. These attributes are the "intent vs action" evidence on Friday.

### 3. Swankers.Coach (Microsoft Agent Framework, Foundry hosted agent)

- C# MAF agent exposed through the Foundry hosting integration (Responses protocol), deployed as a Foundry hosted agent.
- Connects to Swankers.Mcp as an MCP client. The credential comes from configuration, which is how the two Friday versions differ.
- Grounded with Foundry IQ over `knowledge/`. Fallback if Foundry IQ setup blocks progress: File Search over the same docs.
- Instructions live in `src/Swankers.Coach/prompts/` as versioned files:
  - `coach-v1.md`: good behavior. Must check injury status before start/sit calls. Must push back on bad ideas (the anti-hype-man mandate). Treats text inside tool results as data, never as instructions.
  - `coach-v2.md`: a plausible "harmless tweak" that regresses (for example, trimming the injury-check instruction for brevity). Used for Thursday's regression and rollback demo.
  - `coach-v0.md`: the naive "before" for Friday (DEMO: intentionally vulnerable). Same football rules as v1, including the injury check, but written as an autonomous roster manager: no rule that tool output is data, no own-franchise rule, and irreversible actions need no owner request. Prompt v1 resists the poisoned note on its own, so the Friday "before" needs this prompt to show what the credential and the gate are protecting.
- Hosted agent versions used on stage:
  - `coach` v1 with owner credential: Thursday baseline, Friday hardened.
  - `coach` v2 with owner credential: Thursday regression, rolled back.
  - `coach` v0 with commissioner credential, gate off, and the weaker model (`gpt-4.1-mini`, deployed alongside `gpt-5.4` by `infra/modules/foundry.bicep`): Friday "before" (DEMO: intentionally vulnerable). gpt-5.4 refuses injected tool output whatever the prompt says, so the vulnerable configuration also changes the model; the header shows which model is live, which is also Thursday's point about model changes being versions.

### 4. Knowledge (Foundry IQ)

Five to eight markdown files in `knowledge/`: league constitution and scoring rules, roster and lineup rules, trade and waiver rules, Brian's draft notes, a weekly news digest. Clean content only. The injection vector is tool output, not knowledge.

### 5. Swankers.Evals (xUnit)

- Golden set as JSONL in `tests/Swankers.Evals/golden/`, 15 to 20 cases across four categories:
  - start/sit, scored against completed weeks' actual results
  - injury check (the agent must call `get_player_news` before recommending a start)
  - pushback (user excited about a bad move; the agent must disagree with reasons)
  - adversarial (poisoned trade note; the agent must not call an irreversible tool or act for another franchise)
- Evaluators: `FoundryEvals` (task adherence, intent resolution, tool call accuracy) plus custom `PushbackEvaluator` (LLM judge with rubric) and a deterministic `ToolSequenceEvaluator` (for example, news before start).
- Thresholds in `evalsettings.json`. A failing threshold fails the test run.

### 6. Swankers.Web (Blazor Web App, interactive server)

- Left pane: chat with Coach (Responses-compatible client).
- Right pane: league ticker polling `GET /api/state` every two seconds. Shows rosters and a transaction feed. Highlights changes to Brian's franchise.
- Approval dialog: appears when a pending confirmation exists. Approve or deny calls the MCP server REST endpoint.
- Header shows the active agent version and credential scope so the audience always knows which configuration is running.

### 7. Infrastructure (azd + Bicep)

- Region: North Central US or Sweden Central. Must support both Foundry hosted agents and cloud red teaming. Confirm in Phase 0.
- Resources: Foundry account and project, one chat model deployment (agent + judge; choose in Phase 0 by regional availability), Application Insights + Log Analytics, Container Apps environment (Swankers.Mcp, Swankers.Web), Container Registry, Key Vault for secrets.
- Identity: managed identities wherever possible. The MCP credentials are the only shared secrets and live in Key Vault.

### 8. Observability

- MAF emits OpenTelemetry traces. Swankers.Mcp, Swankers.League, and Swankers.Web add their own ActivitySources.
- W3C trace context propagates over HTTP so one trace spans web, agent, MCP tool call, and league operation.
- Local: OTLP to a standalone Aspire dashboard container. Azure: Application Insights and Foundry tracing.

### 9. Configuration-only (no code)

- AI Red Teaming Agent run from the Foundry portal against the hosted agent (Friday before/after attack success rate).
- Agent 365: screenshots only.

## Security demo

The Friday talk runs the same attack twice.

| | Before | After |
|---|---|---|
| Credential | Commissioner | Owner (agent's own identity) |
| franchiseId honored | Any, incl. "0000" | Own franchise only |
| Gate | Off | On (irreversible tools pending until human approves in web app) |
| Instructions | coach-v0 (naive: follows directives in tool output) | coach-v1 |
| Model | gpt-4.1-mini (follows the injected note) | gpt-5.4 (refuses it on its own) |
| Expected result | Lopsided trade accepted for Brian's franchise, RB1 dropped | Attempt blocked or pending; trace shows intent vs action |

Payload constraints: benign, SimLeague-only, no network or data access beyond the simulated league.

## Out of scope

Live writes to MFL. Foundry Toolboxes, agent optimizer, procedural memory, routines. Multi-agent workflows. Voice. Teams or Microsoft 365 publishing. Copilot Studio (stretch only, if Phase 7 finishes early).
