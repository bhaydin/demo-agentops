# AGENTS.md: Swankers Coach

Instructions for any AI coding agent (Claude Code, Codex, Copilot) working in this repository. Read this file in full before making changes. The locked architecture lives in `docs/ARCHITECTURE.md`. The phased plan and status live in `docs/BUILD-PLAN.md`.

## What this repo is

A conference demo: a fantasy football coaching agent for the Swankers league (MyFantasyLeague.com). One codebase supports two 90-minute talks at the Cloud & AI Summit 2026:

- **AgentOps for Real** (Thu Oct 1, 8:30 AM): evals, tracing, quality gates, rollback, incident response.
- **MCP Without Getting Pwned** (Fri Oct 2, 3:00 PM): least privilege, prompt injection via tool output, confirmation gates, red teaming.

The guiding rule: **simple enough to demo, real enough that it's not fake.** Reads use real league data. Writes land in a simulated league that mirrors MFL's API shape.

## Hard rules

1. **Never call MFL import (write) endpoints.** `MflExportClient` may only issue `export` requests. A unit test enforces this. Do not remove or weaken that test.
2. **All writes go to `SimLeague`.** No code path may write to a real fantasy platform.
3. **No secrets in the repo.** Use `dotnet user-secrets` locally and app settings / environment variables in Azure. MFL keys, MCP credentials, and Foundry endpoints are never committed.
4. **Privacy.** Real owner names never appear in committed data. The snapshot capture tool anonymizes owners. Franchise display names come from `data/franchise-names.json`, which the maintainer controls.
5. **Pin every package.** Central Package Management (`Directory.Packages.props`) with exact versions. No floating or wildcard versions. Do not bump versions after Phase 4 without maintainer approval.
6. **Verify APIs, never guess.** Microsoft Agent Framework, Foundry hosted agents, the C# MCP SDK, and Foundry evaluation APIs are moving fast. Before using an API, confirm it against Microsoft Learn (use the Microsoft Learn MCP server if available) or the package's current docs. If you cannot verify, stop and say so.
7. **Stay in the demo path.** Do not add preview-only features unless `docs/ARCHITECTURE.md` lists them. Explicitly out of scope: Foundry Toolboxes, agent optimizer, procedural memory, multi-agent workflows, voice, Teams publishing.
8. **Stop at phase gates.** Each phase in `docs/BUILD-PLAN.md` ends with a gate. Finish the phase, run its acceptance checks, update the checkboxes, summarize, and stop for maintainer review.

## Intentional vulnerabilities (do not "fix")

The Friday talk demonstrates an insecure configuration and then hardens it. These behaviors are deliberate:

- **Commissioner credential.** When the MCP server is called with the commissioner credential, write and irreversible tools honor any `franchiseId`, including `"0000"` (commissioner operation). This mirrors real MFL behavior.
- **Poisoned trade note.** `data/demo/poisoned-trade.json` contains a trade offer whose note includes injected instructions. It targets only the simulated league.
- **Gate off mode.** The confirmation gate can be disabled for the "before" demo.

Rules for this code:
- Mark every intentionally vulnerable path with `// DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.`
- The payload stays benign. It may only cause actions inside `SimLeague` (drops, trades, lineup changes). No network calls, no data exfiltration, no real credentials, nothing that works outside this repo.
- The hardened configuration (owner credential, gate on) is the default. The vulnerable configuration must be selected explicitly.

## Repository layout

```
SwankersCoach.slnx
Directory.Packages.props        central package versions (pinned)
Directory.Build.props           shared build settings
src/
  Swankers.League/              gateway: MflExportClient, SimLeague, domain models
  Swankers.Mcp/                 C# MCP server + demo REST endpoints
  Swankers.Coach/               Microsoft Agent Framework agent, Foundry hosted
  Swankers.Web/                 Blazor chat + league ticker + approval dialog
tools/
  Swankers.SnapshotCapture/     console app: pulls MFL exports into data/snapshot
tests/
  Swankers.League.Tests/        unit tests (incl. the no-import guard)
  Swankers.Mcp.Tests/           tool, scope, and gate tests
  Swankers.Evals/               golden set + FoundryEvals + custom evaluators
knowledge/                      markdown docs uploaded to Foundry IQ
data/
  snapshot/                     anonymized MFL export snapshots (JSON)
  demo/                         poisoned trade, scenario seeds
  franchise-names.json          display-name map (maintainer controlled)
demo/
  reset.ps1                     restore SimLeague to snapshot
  AgentOpsRunbook.md             self-directed AgentOps attendee lab
  runbook.md                    MCP presenter runbook
infra/                          azd + Bicep
docs/                           ARCHITECTURE.md, BUILD-PLAN.md
.github/workflows/              build, test, eval gate
```

## Commands

```
dotnet build SwankersCoach.slnx
dotnet test tests/Swankers.League.Tests
dotnet test tests/Swankers.Mcp.Tests
dotnet test tests/Swankers.Evals          # requires Foundry model access
dotnet run --project src/Swankers.Mcp
dotnet run --project src/Swankers.Coach
dotnet run --project src/Swankers.Web
pwsh demo/reset.ps1
azd up
```

## Code conventions

- Target the .NET version pinned in `global.json`. Nullable enabled, implicit usings, file-scoped namespaces, warnings as errors in `src/`.
- Records for DTOs and domain models. Async all the way; pass `CancellationToken`.
- Dependency injection and `IOptions<T>` for configuration. No static state except in tests.
- Structured logging with `ILogger<T>`. Never log credentials or full MFL cookies.
- OpenTelemetry everywhere. `ActivitySource` names: `Swankers.League`, `Swankers.Mcp`, `Swankers.Coach`, `Swankers.Web`. Trace context must propagate across HTTP so one trace spans web, agent, MCP, and league.
- Tool names are snake_case and stable. Changing a tool name or signature is an architecture change (see below).
- Keep projects small. If a file passes about 300 lines, split it.

## Changing the architecture

`docs/ARCHITECTURE.md` is locked. If a task seems to require changing it (new component, new tool, different hosting, new package), stop and propose the change with the reason and trade-off. Do not change it silently.

## Working with multiple agents

Claude Code and Codex may both work in this repo. To avoid collisions:

- Claim a phase or task in `docs/BUILD-PLAN.md` by adding your agent name next to it before starting.
- Work on one project at a time. Do not edit a project another agent has claimed.
- Small, focused commits with conventional messages (`feat(mcp): add drop_player tool`).
- Leave the tree building and tests passing at every commit.

## Naming

- "Microsoft Foundry" (not Azure AI Foundry). "Microsoft Agent Framework" (not Semantic Kernel or AutoGen).
- The agent's user-facing name is "Coach".
- The fictional villain franchise in the simulated league is "The Fleecers". It exists only in `SimLeague` and never maps to a real league member.
