# Swankers.AgentDeploy

Publishes `src/Swankers.Coach` and deploys it to the shared Microsoft Foundry project as a
**hosted agent** (code bundle: `dotnet_10` runtime, `dependency_resolution: bundled`). No
container image is involved, so it works from the maintainer's ARM64 machine without Docker.

Every `create` makes a new immutable version. The agent endpoint routes 100% of traffic to
one version at a time; `route` moves it. That is the Thursday rollback.

```
pwsh tools/Swankers.AgentDeploy/deploy-coach.ps1        # all three stage versions, routed to v1-owner

dotnet run --project tools/Swankers.AgentDeploy -- list
dotnet run --project tools/Swankers.AgentDeploy -- route --version 2      # e.g. switch to v2-owner
dotnet run --project tools/Swankers.AgentDeploy -- create --label v1-owner --prompt v1 --credential-key Mcp:OwnerCredential --route
dotnet run --project tools/Swankers.AgentDeploy -- delete --version 1     # never the routed one
```

The tool zips the publish folder itself and uploads it with the documented REST call
(multipart `metadata` + `code`, SHA-256 in `x-ms-code-zip-sha256`): the SDK's folder upload
writes Windows separators into the entry names, so the hosted container cannot find
`prompts/` or `knowledge/`, and its typed multipart path is internal in 3.0.0-beta.2.

| Stage | Prompt | Credential key | Used by |
|---|---|---|---|
| `v1-owner` | `v1` | `Mcp:OwnerCredential` | Default; Friday "after" |
| `v2-owner` | `v2` | `Mcp:OwnerCredential` | Thursday regression and rollback |
| `v1-commissioner` | `v1` | `Mcp:CommissionerCredential` | Friday "before" (DEMO: intentionally vulnerable) |

The publish output goes to `<temp>/swankers-coach-publish` by default: `dotnet publish -o`
cannot take a path containing `,` or `;` (MSBuild splits property values there), and this
checkout may live under such a folder.

Settings come from the matching `--` options, the process environment, or the repository's
selected azd environment (the tool runs `azd env get-values` itself, so the commands above
work from a fresh shell after `azd up`): `FOUNDRY_PROJECT_ENDPOINT`, `KEYVAULT_URI`,
`MCP_ENDPOINT`, `AZURE_AI_MODEL_DEPLOYMENT_NAME`.

The output folder is replaced on every publish, so the tool only ever empties a folder it
created (it leaves a `.swankers-coach-publish` marker) and refuses the checkout, anything
inside or above it, a drive root, or a folder with someone else's files.
No version carries a secret: the Coach reads the MCP credential from Key Vault by key at
startup, which is why the agent's identity needs Key Vault Secrets User on `kv-swankers-vxzd`
(the script stores the identity in `COACH_AGENT_PRINCIPAL_ID` and re-provisions to grant it).

Required role for the person running it: Foundry User on the account is enough to create
versions and update routing (Learn, "Hosted agent permissions reference", 2026-09-23);
Foundry Project Manager is what the deploy how-to recommends.
