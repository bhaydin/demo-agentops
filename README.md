# Swankers Coach

A fantasy football coaching agent ("Coach") for the Swankers league on MyFantasyLeague.com. It's a conference demo for two Cloud & AI Summit 2026 talks:

- **AgentOps for Real** (Thu Oct 1): evals, tracing, quality gates, rollback.
- **MCP Without Getting Pwned** (Fri Oct 2): least privilege, prompt injection through tool output, confirmation gates, red teaming.

Reads use real league data from MFL export requests. Writes only ever go to a simulated league (`SimLeague`). Nothing in this repo writes to MFL.

> **Status:** Phase 0 scaffold. Projects compile but don't do anything yet. See [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md).

## Start here

- [AGENTS.md](AGENTS.md): rules for anyone (human or AI agent) changing this repo
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): the locked architecture
- [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md): phases, status, and verified package versions

## Build and test

Requires the .NET SDK pinned in [global.json](global.json).

```
dotnet build SwankersCoach.slnx
dotnet test tests/Swankers.League.Tests
dotnet test tests/Swankers.Mcp.Tests
```

## Run locally

Each service has its own development port (set in `Properties/launchSettings.json`), so they can run side by side:

| Service | Command | URL |
|---|---|---|
| MCP server | `dotnet run --project src/Swankers.Mcp` | `http://localhost:5210` |
| Coach | `dotnet run --project src/Swankers.Coach` | `http://localhost:8088` (the local port Foundry hosted agents use) |
| Web | `dotnet run --project src/Swankers.Web` | `http://localhost:5121` (`--launch-profile https` for `https://localhost:7149`) |

The MCP server needs its credentials: pass `-- --KeyVault:Uri <vault-uri>` to load `Mcp--*` secrets from Key Vault (after `az login`), or set `Mcp__OwnerCredential`, `Mcp__CommissionerCredential`, and `Mcp__DemoAdminKey` as environment variables for a local session. It seeds SimLeague from the latest `data/snapshot/<id>/` (resolved from the repo root) and keeps its state in `data/sim/` (ignored by git). MCP clients authenticate with `Authorization: Bearer <credential>`; the demo REST API under `/api` needs `X-Demo-Admin-Key`.

Coach needs the MCP server running, a Foundry project, and the same Key Vault (it reads the MCP credential by configuration key, so no secret value is ever in its settings):

```
dotnet run --project src/Swankers.Coach -- --KeyVault:Uri <vault-uri> --Coach:ProjectEndpoint <foundry-project-endpoint>
```

Then talk to it over the Responses protocol: `POST http://localhost:8088/responses` with `{"input": "Should I start X or Y this week?"}`. Stage configurations: `--Coach:PromptVersion v2` (Thursday's regression) and `--Coach:McpCredentialKey Mcp:CommissionerCredential` (Friday's "before"). For a local trace view, run `aspire dashboard run` and set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` for both services.

## Layout

| Path | What it is |
|---|---|
| `src/Swankers.League` | League gateway: MFL export client (read only) and `SimLeague` (all writes) |
| `src/Swankers.Mcp` | MCP server and demo REST endpoints |
| `src/Swankers.Coach` | Microsoft Agent Framework agent, hosted in Microsoft Foundry |
| `src/Swankers.Web` | Blazor chat, league ticker, approval dialog |
| `tools/Swankers.SnapshotCapture` | Pulls MFL exports into `data/snapshot` (maintainer runs it) |
| `tests/` | Unit tests and evals |
| `knowledge/` | Markdown docs for Foundry IQ grounding |
| `data/` | Anonymized snapshots, demo scenarios, franchise display names |

## Security note

Some code paths in this repo are **intentionally vulnerable**. They exist so the Friday talk can show an insecure configuration and then harden it. They're marked in code and described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#security-demo). The hardened configuration is the default. The payloads are benign and only affect the simulated league.
