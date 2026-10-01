# Swankers Coach

A fantasy football coaching agent ("Coach") for the Swankers league on MyFantasyLeague.com. It's a conference demo for two Cloud & AI Summit 2026 talks:

- **AgentOps for Real** (Thu Oct 1): evals, tracing, quality gates, rollback.
- **MCP Without Getting Pwned** (Fri Oct 2): least privilege, prompt injection through tool output, confirmation gates, red teaming.

Reads use real league data from MFL export requests. Writes only ever go to a simulated league (`SimLeague`). Nothing in this repo writes to MFL.

> **Status:** Phases 0–3 done (league gateway, MCP server, Coach agent, local tracing); Phase 4 adds the Azure deployment. See [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md).

## Start here

- [AGENTS.md](AGENTS.md): rules for anyone (human or AI agent) changing this repo
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): the locked architecture
- [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md): phases, status, and verified package versions
- [demo/AgentOpsRunbook.md](demo/AgentOpsRunbook.md): attendee lab for running locally, comparing evals, inspecting traces, and practicing recovery with your own resources
- [demo/runbook.md](demo/runbook.md): presenter's October 2 MCP session, before/contained/hardened demos, approval steps, recovery, and rehearsal checklist

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

The web app is the stage: chat with the *deployed* Coach on the left (through its Foundry agent endpoint, so whatever version is routed answers), the league ticker on the right (polls the MCP server's `/api/state` every two seconds), an approval dialog whenever the confirmation gate holds an action, and a header with the routed version, its prompt and credential, and the gate state. It needs the MCP server's base URL and demo admin key, and the Foundry project:

```
dotnet run --project src/Swankers.Web -- --KeyVault:Uri <vault-uri> --Mcp:BaseUrl http://localhost:5210 --Coach:ProjectEndpoint <foundry-project-endpoint>
```

Locally, set `AZURE_TOKEN_CREDENTIALS=dev` so the app's default credential skips the managed-identity probe, which can take two minutes on a machine that is not in Azure. The page is for the presenter only, because it chats as the server's Foundry identity and approves with the demo admin key: it asks for the presenter key at `/login` (Key Vault secret `Web--PresenterKey`, or `--Web:PresenterKey <key>` for a local session) and then keeps a cookie session; "sign out" in the header ends it. Point `Mcp:BaseUrl` at the deployed MCP server (`MCP_BASE_URL` in the azd environment) to see the same league the deployed Coach acts on. `tests/Swankers.Web.Tests` runs the page in-process with a fake league and a fake Coach; its live test talks to the deployed Coach when `FOUNDRY_PROJECT_ENDPOINT` is set (`COACH_LIVE_MESSAGE="Drop X || Yes, do it"` rehearses a gated action while the page is open).

## Deploy to Azure

Two steps: `azd up` for the Container Apps services, then the Coach hosted agent. Needs the Azure Developer CLI (1.32+), Azure CLI (2.80+), a login to the subscription (`az login`, `azd auth login`), the shared resources in `rg-swankers-shared` (Key Vault `kv-swankers-vxzd` and the Foundry account/project from `infra/modules/foundry.bicep`, deployed once by the maintainer), and the Foundry User role on that account.

```
azd env new swankers-dev --location northcentralus
azd env set AZURE_TAG_PRIMARY_OWNER <you@example.com>     # policy tags; Client and ExpectedDeleteDate have defaults
azd up                                                     # rg-swankers-dev: Log Analytics, App Insights, ACR, Container Apps (mcp, web)
pwsh tools/Swankers.AgentDeploy/deploy-coach.ps1           # Coach versions v1-owner, v2-owner, v0-commissioner and v0-owner (both on gpt-4.1-mini); routed to v1-owner
```

- Images are built in the environment's Container Registry (`remoteBuild`), so no local Docker is needed.
- The MCP server is public behind its credentials: `MCP_ENDPOINT` (streamable HTTP) and `MCP_BASE_URL/api` (demo REST, `X-Demo-Admin-Key`). Secrets still come from the shared vault; the container apps read them with managed identity.
- Coach runs as a Foundry hosted agent (code bundle, no image) in the shared project. Each `create` is an immutable version; `dotnet run --project tools/Swankers.AgentDeploy -- route --version <n>` moves the endpoint (the Thursday rollback), `-- list` shows what is live. See [tools/Swankers.AgentDeploy/README.md](tools/Swankers.AgentDeploy/README.md).
- Friday "before" (DEMO, intentionally vulnerable): `azd env set MCP_COMMISSIONER_GATE_ENABLED false` and `azd provision` turn the gate off for commissioner-credential calls; the hardened default is on. The web app's header shows the gate state and the routed version, so the audience sees which configuration is live.
- The web app (`WEB_URL`) reaches the Coach through its agent endpoint and the MCP server through `Mcp__BaseUrl`, both set by Bicep; its identity holds Foundry User on the account and Key Vault Secrets User for the demo admin key and the presenter key (`Web--PresenterKey`, created once by the maintainer). Anyone else who finds the URL gets the sign-in page and nothing more.
- Traces: `appi-swankers-dev` in Application Insights and the Foundry project's Tracing page (the project is connected to the same resource).
- Stage scripts: `pwsh demo/reset.ps1 [-Scenario poisoned-trade]` restores SimLeague (about a second, plus the Key Vault read); `pwsh demo/stage.ps1 -Preset thursday-good|thursday-regressed|friday-before|friday-contained|friday-after` routes the Coach by stage label (`AgentDeploy route --label`), resets, and seeds what the preset needs. The commissioner gate flag is provisioned, not routed; the script warns when it does not match the preset.
- `azd down` removes only `rg-swankers-dev`; the vault, the Foundry account, and the model deployment stay.

## Evals

`tests/Swankers.Evals` holds the golden set (`golden/*.jsonl`, 17 cases: start/sit, injury check, pushback, adversarial) and the gate. The gate runs the Coach in-process, built exactly like the hosted version but with the MCP server hosted in the test on the repo's real snapshot, then scores each case with deterministic rules (`ToolSequenceEvaluator`: expected and forbidden tools, `get_player_news` before any start/sit call, lineup contents, own-franchise scope), an LLM judge for pushback, and Foundry's `task_adherence` / `intent_resolution` / `tool_call_accuracy` evaluators (portal report links in the output; `tool_call_accuracy` is reported but not gated, because its judge penalizes the read-everything pattern v1 must follow). Thresholds live in `evalsettings.json`.

```
azd env get-value FOUNDRY_PROJECT_ENDPOINT              # the gate skips itself when this is not set
$env:FOUNDRY_PROJECT_ENDPOINT = "<endpoint>"; $env:COACH_PROMPT_VERSION = "v2"
dotnet test tests/Swankers.Evals -c Release --logger "console;verbosity=normal"
# COACH_EVAL_CASES=adv-01,adv-02 runs a subset while iterating on a prompt or scenario (local only)
```

Reports land in `artifacts/evals/` (ignored). In CI, `.github/workflows/evals.yml` is dispatched manually from **main**, requires your approval in the `evals` environment, and runs one job per prompt version (v1 green, v2 red on the injury-check category). With `promote: true`, a green version is deployed and routed only after your separate approval in `foundry`. PRs run model-free tests without Azure access. `infra/ci-identity.ps1` sets up separate environment-only OIDC identities: `swankers-ci-evals` has Foundry User on the isolated eval project plus Cognitive Services OpenAI User for graders; `swankers-ci-deploy` can create and route hosted versions. Both environments allow only the main branch. See [repository and deployment authorization](docs/REPOSITORY-SECURITY.md) for required checks, owner review, the sole-maintainer PR bypass, and setup/verification commands.

## Layout

| Path | What it is |
|---|---|
| `src/Swankers.League` | League gateway: MFL export client (read only) and `SimLeague` (all writes) |
| `src/Swankers.Mcp` | MCP server and demo REST endpoints |
| `src/Swankers.Coach` | Microsoft Agent Framework agent, hosted in Microsoft Foundry |
| `src/Swankers.Web` | Blazor chat, league ticker, approval dialog |
| `tools/Swankers.SnapshotCapture` | Pulls MFL exports into `data/snapshot` (maintainer runs it) |
| `tools/Swankers.AgentDeploy` | Publishes Coach to Foundry as hosted agent versions; routes the endpoint |
| `infra/` | azd Bicep: the disposable environment (`main.bicep`) and the shared Foundry module |
| `tests/` | Unit tests and evals |
| `knowledge/` | Markdown docs for Foundry IQ grounding |
| `data/` | Anonymized snapshots, demo scenarios, franchise display names |

## Security note

Some code paths in this repo are **intentionally vulnerable**. They exist so the Friday talk can show an insecure configuration and then harden it. They're marked in code and described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#security-demo). The hardened configuration is the default. The payloads are benign and only affect the simulated league.
