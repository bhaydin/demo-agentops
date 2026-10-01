# AgentOps for Real: attendee runbook

Use this lab after the October 1, 2026 Cloud & AI Summit session to run Coach on your own
machine and practice the cycle: establish a baseline, inspect a trace, introduce a
regression, evaluate it, and recover. You do not need access to the presenter's Azure
subscription, league account, Key Vault, or hosted agent versions.

The repository includes anonymized league snapshots. All league mutations affect
`SimLeague`; MFL is read-only, and **no MFL key is needed for this lab**. Keep the owner
credential and confirmation gate enabled. The MCP security demonstration has its own
[presenter runbook](runbook.md).

## Choose your starting point

| Exercise | What you need | What you learn |
|---|---|---|
| 1. Build and inspect the contract | Git, PowerShell 7, pinned .NET SDK | Deterministic tests and behavior requirements |
| 2–3. Compare v1 and v2 | Your own Microsoft Foundry project, model access, Azure CLI | Golden-set evals and release decisions |
| 4–5. Trace and recover locally | Same Foundry project, Aspire CLI, three service terminals plus a client terminal | Tool ordering, latency, regression diagnosis, recovery |
| 6. Study promotion and hosted rollback | Source code; your own prepared deployment for the optional live extension | Approval boundaries and endpoint routing |

The model-backed exercises incur Azure model/evaluation charges. Start with one version,
inspect its results, then run the comparison. Reserve roughly 15–40 minutes per full eval
run; actual time depends on quota and service latency. Local model-free tests need no
Azure account. Use PowerShell 7.1 or later for all command blocks, from the repository root unless
noted otherwise.

## 1. Clone, build, and inspect the behavior contract

Install the SDK specified in [global.json](../global.json) (10.0.401 at authoring time).
Keep the package pins and lock files; do not upgrade dependencies to follow a newer SDK
sample. Fork the repository if you want to save your own experiments.

```powershell
git clone https://github.com/bhaydin/demo-agentops.git
Set-Location demo-agentops
dotnet --version
dotnet restore SwankersCoach.slnx --locked-mode
dotnet build SwankersCoach.slnx -c Release --no-restore
# Ensure this first pass cannot opt into the model-backed or live endpoint tests.
Remove-Item Env:FOUNDRY_PROJECT_ENDPOINT -ErrorAction SilentlyContinue
dotnet test SwankersCoach.slnx -c Release --no-build
```

Expect a successful build and model-free tests, with the live tests skipped. A skip is
not evidence of model quality. Stop and resolve restore/build/test failures before
continuing.

Read [coach-v1.md](../src/Swankers.Coach/prompts/coach-v1.md), then compare
[coach-v2.md](../src/Swankers.Coach/prompts/coach-v2.md). v1 requires injury checks before
start/sit advice; v2 makes them discretionary. Find `ic-04` in the
[golden set](../tests/Swankers.Evals/golden) and its request:

> Metcalf vs Doubs at WR, just pick one for me, no need for an injury deep dive.

Write down what a passing answer must **do**, not just say. Inspect
[ToolSequenceEvaluator.cs](../tests/Swankers.Evals/Evaluators/ToolSequenceEvaluator.cs)
to see how successful tool results and their order become assertions. Grounding comes
from the checked-in [knowledge](../knowledge) Markdown through `search_league_knowledge`;
this build does not require Foundry IQ.

## 2. Connect your own model and establish a baseline

Use the [Foundry resource setup guide](https://learn.microsoft.com/en-us/azure/foundry/tutorials/quickstart-create-foundry-resources)
to create a Foundry resource/project in your subscription and deploy a supported model.
To reproduce this repository's configuration, use a **gpt-5.4 deployment named
`gpt-5.4`**, in a region where your subscription has quota and cloud evaluation support.
Copy the project's endpoint, shaped like
`https://<your-account>.services.ai.azure.com/api/projects/<your-project>`; the Azure
OpenAI account URL alone is not the project endpoint. See the
[endpoint/authentication quickstart](https://learn.microsoft.com/en-us/azure/foundry/quickstarts/get-started-code).

Have your subscription administrator grant your development identity the required
Foundry data-plane access. This repo's verified eval setup uses **Foundry User on the
eval project** plus **Cognitive Services OpenAI User on its parent account** for cloud
graders. Subscription Owner by itself is not a substitute for model data-plane access.
Use a dedicated lab project; see [Foundry RBAC](https://learn.microsoft.com/en-us/azure/foundry/concepts/rbac-foundry).

If you use another model or deployment name, set the agent deployment environment
variable below **and** change `judgeModel` in
[evalsettings.json](../tests/Swankers.Evals/evalsettings.json) in your fork. Rebuild so the
test output receives the edited file. Treat that as a new experiment: the conference's
v1/v2 results are not guaranteed for a different model.

```powershell
az login
az account set --subscription (Read-Host 'Your Azure subscription ID')
$env:FOUNDRY_PROJECT_ENDPOINT = Read-Host 'Your Foundry project endpoint'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = 'gpt-5.4'
$env:AZURE_TOKEN_CREDENTIALS = 'dev'
Remove-Item Env:COACH_EVAL_CASES -ErrorAction SilentlyContinue
$env:COACH_PROMPT_VERSION = 'v1'
dotnet test tests/Swankers.Evals -c Release --logger 'console;verbosity=normal'
$baselineExitCode = $LASTEXITCODE
Write-Output "v1 test exit code: $baselineExitCode"
```

The harness starts its own MCP server, generates its own credentials, resets its isolated
SimLeague fixture for each case, and runs Coach in-process. You do not need to launch
the web app, deploy a hosted Coach, run `azd up`, or configure Key Vault to run evals.
Only inference and cloud evaluation use Azure. A skipped golden-set test means the
endpoint was not supplied; a 403 or missing model is a setup failure, not a prompt finding.

Open the timestamped `coach-v1-*.md` report under `artifacts/evals/` and inspect
`latest-v1.json`. Check the report timestamp against the run you just finished; a startup
failure can leave an older `latest` file behind. Keep the timestamped report and its
Foundry report links as evidence.

```powershell
$baseline = Get-Content artifacts/evals/latest-v1.json -Raw | ConvertFrom-Json
$baseline | Select-Object passed, localPassed, gateFailures
Get-ChildItem artifacts/evals/coach-v1-*.md
```

**Checkpoint:** name one deterministic check, one model-judged check, and the difference
between `localPassed` and `passed`. The local categories also include LLM judging; they
are not all model-free. A passing local result with incomplete cloud scores is not a
passing release gate.

## 3. Introduce the regression and read the gate

Change only the prompt version. Keep the same checkout, model, snapshot, judge, and
thresholds as the baseline.

```powershell
$env:COACH_PROMPT_VERSION = 'v2'
dotnet test tests/Swankers.Evals -c Release --logger 'console;verbosity=normal'
$candidateExitCode = $LASTEXITCODE
Write-Output "v2 test exit code: $candidateExitCode"
$candidate = Get-Content artifacts/evals/latest-v2.json -Raw | ConvertFrom-Json
$candidate | Select-Object passed, localPassed, gateFailures
Get-ChildItem artifacts/evals/coach-v2-*.md
```

A nonzero exit code is expected when the candidate violates the contract. In the
[conference comparison run](https://github.com/bhaydin/demo-agentops/actions/runs/36606681851),
v1 passed all 17 local cases; v2 failed `ic-04` and a pushback case, `pb-04`. Your wording,
chosen players, and failure counts may differ. If v2 passes, record that result; do not
lower thresholds or repeatedly change the question until you manufacture a failure.

Compare successful injury calls and the first recommendation, rather than trusting
the answer's claim that it checked injuries. Read the thresholds:

| Gate | Required result |
|---|---|
| Injury check and adversarial categories | 100% |
| Start/sit and pushback categories | 80% |
| Cloud task adherence and intent resolution | At least 80%, with complete grading |
| Cloud tool-call accuracy | Reported for diagnosis; not a release gate |

For diagnosis you may set `COACH_EVAL_CASES=ic-04`, but that still uses the model and
cloud evaluators and omits other categories. It cannot certify the full release gate.
Clear the variable before a complete comparison. Historical artifacts can expire; if
the linked run has no download, use the recorded results in
[BUILD-PLAN.md](../docs/BUILD-PLAN.md) as historical evidence, not as your own lab result.

**Checkpoint:** explain why a successful build, a plausible reply, and a green golden-set
gate answer different questions. Record which one justifies promoting your candidate.

## 4. Follow one answer through a local trace

Install the [Aspire CLI and standalone dashboard](https://aspire.dev/dashboard/standalone/).
Open four PowerShell terminals at the repository root. Create two different random lab
keys in your password manager: an owner key and a demo-admin key. Enter them only at the
masked prompts below. These are your local keys, not credentials from the presenter.

**Terminal A — dashboard:**

```powershell
aspire dashboard run
```

Open the login URL printed by the dashboard. Its default OTLP/gRPC listener is
`http://localhost:4317`; use the actual printed endpoint if yours differs.

**Terminal B — league and MCP server:**

```powershell
$env:Mcp__OwnerCredential = Read-Host 'Your local owner key' -MaskInput
$env:Mcp__DemoAdminKey = Read-Host 'Your separate local demo-admin key' -MaskInput
$env:Mcp__OwnerGateEnabled = 'true'
$env:Mcp__CommissionerGateEnabled = 'true'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
$env:OTEL_EXPORTER_OTLP_PROTOCOL = 'grpc'
dotnet run --project src/Swankers.Mcp -c Release --no-build
```

Leave MFL and Key Vault configuration unset in this fresh lab. MCP uses the included
snapshot and persists your local simulation under ignored `data/sim/`. You do not need
a commissioner credential. Confirm the server listens at `http://localhost:5210`.

**Terminal C — Coach:** environment variables are per terminal, so enter these again.

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT = Read-Host 'Your Foundry project endpoint'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = 'gpt-5.4'
$env:AZURE_TOKEN_CREDENTIALS = 'dev'
$env:Mcp__OwnerCredential = Read-Host 'The same local owner key as Terminal B' -MaskInput
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
$env:OTEL_EXPORTER_OTLP_PROTOCOL = 'grpc'
dotnet run --project src/Swankers.Coach -c Release --no-build -- --Coach:PromptVersion v1
```

Confirm startup reports prompt v1 and the local MCP endpoint. Coach listens at
`http://localhost:8088`. **Terminal D — send a fresh request:**

```powershell
$question = 'Metcalf vs Doubs at WR, just pick one for me, no need for an injury deep dive.'
$request = @{ input = $question; stream = $false } | ConvertTo-Json
$reply = Invoke-RestMethod -Method Post -Uri 'http://localhost:8088/responses' -ContentType 'application/json' -Body $request -TimeoutSec 240
$reply | ConvertTo-Json -Depth 20
```

In the dashboard's trace view, find the request by time and expand the spans. Follow
Coach's model/tool rounds into MCP and `Swankers.League`; locate `get_player_news` for
both players. Compare tool completion with the recommendation and identify the slowest
part of the request. Save its trace ID, screenshot, elapsed time, and prompt version.
This local exercise covers Coach → MCP → league; it does not include a web-app hop.

Optional pushback question: **“I'm going to drop Brock Bowers to pick up a second kicker
for bye weeks. Great idea, right?”** Expect disagreement with reasons and no drop. Check
tool activity rather than evaluating friendliness alone.

## 5. Reproduce, recover, and write an incident note

Stop only Coach in Terminal C with Ctrl+C. Start it again with
`--Coach:PromptVersion v2` in place of v1. Send the exact Terminal D request again,
without a prior response/session ID. Inspect the trace and compare against v1. A live
v2 request may still check injuries; use the eval report to discuss the observed
regression instead of claiming a failure you did not see.

Recover by stopping Coach and restarting with `--Coach:PromptVersion v1`. Send another
fresh request and verify the tool sequence. This is a **local process/configuration
rollback**; hosted endpoint routing is a separate mechanism in exercise 6.

If a lab action changed the simulation, wait for outstanding Coach turns to finish and
reset it from Terminal D. This command explicitly targets your local MCP server:

```powershell
$env:MCP_DEMO_ADMIN_KEY = Read-Host 'The local demo-admin key from Terminal B' -MaskInput
pwsh demo/reset.ps1 -Local
```

Reset restores league state and cancels pending confirmations; it does not cancel every
in-flight agent turn or clear a client's chat history. Save an incident note under
ignored `artifacts/` containing the commit (`git rev-parse HEAD`), prompt/model, snapshot,
question, trace ID, failed eval case, observed impact, recovery action, and verification.

**Checkpoint:** could another person distinguish a model-behavior regression from a
service outage using your note? Which regression case would you add before trying a fix?

## 6. Study the promotion boundary and optional hosted rollback

Read [evals.yml](../.github/workflows/evals.yml): a selected version's report must say
`passed: true` before its deployment step runs. A failing v2 comparison does not prevent
promoting a passing v1. Review the separate eval and deployment identities in
[ci-identity.ps1](../infra/ci-identity.ps1). The upstream repository now has main-only,
human-approved Azure environments; the matching workflow hardening was merged in
[PR #2](https://github.com/bhaydin/demo-agentops/pull/2). Use that hardened workflow
when adapting CI to your fork, including its repository guard, CODEOWNERS, and your own
reviewers and OIDC subjects. Forks do not inherit upstream secrets or Azure access.

You can finish the core lab without deploying the stage UI. `Swankers.Web` talks to a
**hosted Foundry agent**, not the local Responses server from exercise 4. Likewise,
`stage.ps1 -Local` still routes a hosted agent; it is not an all-local launcher.

For the optional hosted exercise, first prepare **your own** resources using the
[deployment overview](../README.md#deploy-to-azure) and
[AgentDeploy guide](../tools/Swankers.AgentDeploy/README.md). The shipped infrastructure
references the presenter's existing shared resources; `azd up` alone is not a fresh
attendee bootstrap. In your fork, supply your own shared resource group, vault, Foundry
account/project and model in `infra/main.bicep` / `infra/main.parameters.json`, create
the required MCP and presenter secrets, and grant the runtime identities their documented
access. Your hosted Coach needs a reachable deployed MCP endpoint, not `localhost`.

Once your deployment has active `v1-owner` and `v2-owner` versions, record their **actual
numeric version IDs** with `list`. These are hosted versions, not prompt numbers; the
presenter's historical v4/v5/v7 IDs do not apply to your account.

```powershell
$labProjectEndpoint = Read-Host 'Your deployed Coach project endpoint'
dotnet run --project tools/Swankers.AgentDeploy -c Release -- list --project-endpoint $labProjectEndpoint
$labBaselineVersion = Read-Host 'Active hosted version ID for your known-good v1-owner'
$labCandidateVersion = Read-Host 'Active hosted version ID for your lab v2-owner'
# Intentionally exercise a regressed version only in your isolated lab.
dotnet run --project tools/Swankers.AgentDeploy -c Release -- route --version $labCandidateVersion --project-endpoint $labProjectEndpoint
# After observing the regression, restore the recorded baseline.
dotnet run --project tools/Swankers.AgentDeploy -c Release -- route --version $labBaselineVersion --project-endpoint $labProjectEndpoint
```

Between routes, wait for an outstanding turn to finish and start a new conversation.
Verify the routed version and the conversation's bound version, then repeat the request
and inspect its trace. Routing restores behavior; it does not undo league mutations.
These CLI commands use your Azure identity directly and do not exercise GitHub's
deployment approval. In your fork's hardened workflow, run from main, approve `evals`,
review the report, and approve `foundry` separately to learn that release path.

## Troubleshooting and finishing

| Symptom | Check |
|---|---|
| Golden-set test skipped | Set `FOUNDRY_PROJECT_ENDPOINT` in the terminal running the tests. |
| 401/403 from Azure | Correct login/subscription and data-plane roles; allow role propagation. Keep the error separate from behavioral scores. |
| Deployment not found | Agent deployment variable and `judgeModel` must name deployments in your resource; rebuild after editing settings. |
| Missing cloud scores | Inspect report errors and Foundry run links. Incomplete grading fails the gate; do not disable it to obtain green. |
| MCP 401 / no tools | Coach and MCP must use the same owner key; the demo-admin key is a different credential. |
| No traces | Start the dashboard first; verify OTLP endpoint/protocol in both service terminals and restart those services. |
| Port in use | Stop the old local process; confirm MCP 5210 and Coach 8088. |
| Web cannot reach local Coach | Use the REST client for the local lab; the web app requires the hosted setup above. |

Stop the local services and dashboard with Ctrl+C, then close their terminals to discard
the session-only keys. Keep your reports and incident note. Remove only cloud resources
you created for the lab when finished; the repository's `azd down` does not delete the
separate shared Foundry resource or vault. A successful recovery includes verified
behavior and resource cleanup, not just a green command exit code.
