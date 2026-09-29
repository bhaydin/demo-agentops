#!/usr/bin/env pwsh
<#
.SYNOPSIS
One-time setup of the GitHub Actions identities for evals.yml: two Entra applications with
GitHub OIDC federated credentials (no secrets), least-privilege roles on the shared Foundry
account, and the repository secrets, variables, and protected environment the workflow reads.

.DESCRIPTION
Two identities, because eval jobs run pull-request code (Codex Phase 5 P1):

  swankers-ci-evals   main + pull requests   custom role "Swankers Evals Runner": responses,
                                             evaluations, agents read. Cannot create or route
                                             hosted agent versions.
  swankers-ci-deploy  environment:foundry    Foundry User: creates and routes agent versions.
                                             Only a job that passed the environment's required
                                             review can obtain this token or its client id.

GitHub's OIDC subject can carry the owner and repository ids ("repo:owner@123/name@456:..."),
so both the classic and the id-qualified subject are registered for every credential.

Run once by the maintainer after `az login` (Owner on the subscription) and `gh auth login`.
Idempotent: existing apps, service principals, credentials, role definition, and assignments
are reused. The retired single-purpose application `swankers-ci` is removed if present.
#>
[CmdletBinding()]
param(
    [string] $Repo = 'bhaydin/demo-agentops',
    [string] $EvalsAppName = 'swankers-ci-evals',
    [string] $DeployAppName = 'swankers-ci-deploy',
    [string] $RetiredAppName = 'swankers-ci',
    [string] $SharedResourceGroup = 'rg-swankers-shared',
    [string] $FoundryAccount = 'foundry-swankers-vxzd',
    [string] $Environment = 'foundry',
    [string] $EvalsRoleName = 'Swankers Evals Runner'
)

$ErrorActionPreference = 'Stop'
$foundryUserRole = '53ca6127-db72-4b80-b1b0-d745d6d5456d' # Foundry User (formerly Azure AI User)

# Always returns a string (empty when az printed nothing), so callers can .Trim() safely.
function Invoke-Az { param([string[]] $AzArgs) $out = & az @AzArgs 2>&1; if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs[0..1] -join ' ') failed: $out" }; return (@($out) | ForEach-Object { "$_" }) -join "`n" }

# `az ad app list --display-name` matches by prefix, so every lookup filters on the exact name.
function Get-AppExact {
    param([string] $Name)
    return (Invoke-Az @('ad', 'app', 'list', '--display-name', $Name, '--query', "[?displayName=='$Name'] | [0]", '-o', 'json')) | ConvertFrom-Json
}

function Ensure-App {
    param([string] $Name)
    $app = Get-AppExact $Name
    if (-not $app) {
        Write-Host "Creating application $Name"
        $app = (Invoke-Az @('ad', 'app', 'create', '--display-name', $Name, '-o', 'json')) | ConvertFrom-Json
    }
    $sp = (Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$($app.appId)'", '--query', '[0]', '-o', 'json')) | ConvertFrom-Json
    if (-not $sp) {
        Write-Host "Creating service principal for $Name"
        $sp = (Invoke-Az @('ad', 'sp', 'create', '--id', $app.appId, '-o', 'json')) | ConvertFrom-Json
    }
    Write-Host "${Name}: application $($app.appId), service principal $($sp.id)"
    return @{ App = $app; Sp = $sp }
}

function Ensure-FederatedCredentials {
    param($App, [hashtable] $Subjects)
    $existing = (Invoke-Az @('ad', 'app', 'federated-credential', 'list', '--id', $App.id, '-o', 'json')) | ConvertFrom-Json
    foreach ($name in $Subjects.Keys) {
        if ($existing | Where-Object { $_.subject -eq $Subjects[$name] }) { Write-Host "  credential exists: $($Subjects[$name])"; continue }
        $file = New-TemporaryFile
        @{ name = $name; issuer = 'https://token.actions.githubusercontent.com'; subject = $Subjects[$name]; audiences = @('api://AzureADTokenExchange') } | ConvertTo-Json | Set-Content $file
        Write-Host "  adding credential $($Subjects[$name])"
        Invoke-Az @('ad', 'app', 'federated-credential', 'create', '--id', $App.id, '--parameters', "@$file") | Out-Null
        Remove-Item $file -Force
    }
}

$subscription = (Invoke-Az @('account', 'show', '--query', 'id', '-o', 'tsv')).Trim()
$tenant = (Invoke-Az @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Trim()
$accountScope = (Invoke-Az @('cognitiveservices', 'account', 'show', '-n', $FoundryAccount, '-g', $SharedResourceGroup, '--query', 'id', '-o', 'tsv')).Trim()
$repoInfo = gh api "repos/$Repo" --jq '{id: .id, ownerId: .owner.id, owner: .owner.login, name: .name}' | ConvertFrom-Json
$repoForms = @($Repo, "$($repoInfo.owner)@$($repoInfo.ownerId)/$($repoInfo.name)@$($repoInfo.id)")

# Custom role for eval runs: the Foundry data plane (model calls through the project, cloud
# evaluations, agent reads) with hosted-agent mutation explicitly excluded. The project's
# Responses gateway answers 403 without a body, so the grant is the AIServices and OpenAI data
# planes minus the write actions that could create, route, or delete agents; evals.yml probes
# that an agent create still returns 403 for this identity. Action names verified with
# `az provider operation show --namespace Microsoft.CognitiveServices`.
$roleFile = New-TemporaryFile
@{
    Name = $EvalsRoleName
    IsCustom = $true
    Description = 'Swankers Coach eval runs: Foundry data plane for model calls, cloud evaluations, and agent reads. Agent, version, and deployment writes are excluded.'
    Actions = @('Microsoft.CognitiveServices/*/read')
    NotActions = @()
    # The Foundry data plane as Foundry User grants it (Microsoft.CognitiveServices/*): the
    # project's Responses gateway still answered 403 with only accounts/AIServices/* and
    # accounts/OpenAI/*, and it names no action. The mutation exclusions below are what matter.
    DataActions = @(
        'Microsoft.CognitiveServices/*'
    )
    NotDataActions = @(
        'Microsoft.CognitiveServices/accounts/AIServices/agents/write',
        'Microsoft.CognitiveServices/accounts/AIServices/agents/delete',
        'Microsoft.CognitiveServices/accounts/AIServices/managed-deployments/action',
        'Microsoft.CognitiveServices/accounts/AIServices/managedComputeDeployments/write',
        'Microsoft.CognitiveServices/accounts/AIServices/managedComputeDeployments/delete',
        'Microsoft.CognitiveServices/accounts/AIServices/fine_tuning_deployments/write',
        'Microsoft.CognitiveServices/accounts/OpenAI/assistants/*'
    )
    AssignableScopes = @("/subscriptions/$subscription/resourceGroups/$SharedResourceGroup")
} | ConvertTo-Json -Depth 4 | Set-Content $roleFile
$roleExists = (Invoke-Az @('role', 'definition', 'list', '--name', $EvalsRoleName, '--custom-role-only', 'true', '--query', '[0].id', '-o', 'tsv')).Trim()
if ($roleExists) { Write-Host "Updating role definition '$EvalsRoleName'"; Invoke-Az @('role', 'definition', 'update', '--role-definition', "@$roleFile") | Out-Null }
else { Write-Host "Creating role definition '$EvalsRoleName'"; Invoke-Az @('role', 'definition', 'create', '--role-definition', "@$roleFile") | Out-Null }
Remove-Item $roleFile -Force

# Evals identity: main branch and pull requests; never the deployment environment.
$evals = Ensure-App $EvalsAppName
$evalsSubjects = @{}
for ($i = 0; $i -lt $repoForms.Count; $i++) {
    $suffix = if ($i -eq 0) { '' } else { '-ids' }
    $evalsSubjects["github-main$suffix"] = "repo:$($repoForms[$i]):ref:refs/heads/main"
    $evalsSubjects["github-pull-request$suffix"] = "repo:$($repoForms[$i]):pull_request"
}
Ensure-FederatedCredentials $evals.App $evalsSubjects
Write-Host "Assigning '$EvalsRoleName' to $EvalsAppName on $FoundryAccount"
Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $evals.Sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $EvalsRoleName, '--scope', $accountScope, '-o', 'none') | Out-Null

# Deploy identity: only the protected environment can mint its token.
$deploy = Ensure-App $DeployAppName
$deploySubjects = @{}
for ($i = 0; $i -lt $repoForms.Count; $i++) {
    $suffix = if ($i -eq 0) { '' } else { '-ids' }
    $deploySubjects["github-env-$Environment$suffix"] = "repo:$($repoForms[$i]):environment:$Environment"
}
Ensure-FederatedCredentials $deploy.App $deploySubjects
Write-Host "Assigning Foundry User to $DeployAppName on $FoundryAccount"
Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $deploy.Sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $foundryUserRole, '--scope', $accountScope, '-o', 'none') | Out-Null

# Retire the single identity that carried both permissions (exact name: a prefix match would
# catch swankers-ci-evals and swankers-ci-deploy).
$retired = Get-AppExact $RetiredAppName
if ($retired -and $retired.displayName -eq $RetiredAppName) {
    Write-Host "Removing retired application $RetiredAppName ($($retired.appId)) and its role assignments"
    $retiredSp = (Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$($retired.appId)'", '--query', '[0].id', '-o', 'tsv')).Trim()
    if ($retiredSp) { & az role assignment delete --assignee $retiredSp --scope $accountScope -o none 2>$null }
    Invoke-Az @('ad', 'app', 'delete', '--id', $retired.id) | Out-Null
}

# Protected environment first (the deploy client id lives there), then secrets and variables.
$me = gh api user --jq .id
$body = New-TemporaryFile
@{ reviewers = @(@{ type = 'User'; id = [int]$me }) } | ConvertTo-Json -Depth 3 | Set-Content $body
gh api -X PUT "repos/$Repo/environments/$Environment" --input $body | Out-Null
Remove-Item $body -Force

gh secret set AZURE_CLIENT_ID --repo $Repo --body $evals.App.appId
gh secret set AZURE_TENANT_ID --repo $Repo --body $tenant
gh secret set AZURE_SUBSCRIPTION_ID --repo $Repo --body $subscription
gh secret set AZURE_DEPLOY_CLIENT_ID --repo $Repo --env $Environment --body $deploy.App.appId
$values = @{}
foreach ($line in (& azd env get-values)) { if ($line -match '^([^=]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"') } }
foreach ($key in 'FOUNDRY_PROJECT_ENDPOINT', 'AZURE_AI_MODEL_DEPLOYMENT_NAME', 'MCP_ENDPOINT', 'KEYVAULT_URI') {
    if (-not $values[$key]) { throw "azd environment has no $key; run azd up first." }
    gh variable set $key --repo $Repo --body $values[$key]
}

Write-Host "Done. Eval jobs log in as $EvalsAppName (no agent writes); the promote job logs in as $DeployAppName after approval in the '$Environment' environment."
