#!/usr/bin/env pwsh
<#
.SYNOPSIS
One-time setup of the GitHub Actions identities for evals.yml: two Entra applications with
GitHub OIDC federated credentials (no secrets), least-privilege roles on the shared Foundry
account, and the repository secrets, variables, and protected environment the workflow reads.

.DESCRIPTION
Two identities with separate protected, main-only environments:

  swankers-ci-evals   environment:evals      Foundry User scoped to the evals project only
                                             (swankers-evals): model calls and cloud
                                             evaluations there; no access to the coach project
                                             or its hosted agent. Scope, not data actions,
                                             does the separation: the project Responses
                                             gateway refuses model calls without agents/write.
                                             Plus Cognitive Services OpenAI User on the account
                                             (no agent actions) so the cloud graders can call
                                             the judge model.
  swankers-ci-deploy  environment:foundry    Foundry User on the account: creates and routes
                                             agent versions. Only a job that passed the
                                             environment's required review can obtain this
                                             token or its client id.

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
    [string] $EvalsProject = 'swankers-evals',
    [string] $Environment = 'foundry',
    [string] $EvalsEnvironment = 'evals',
    [string] $RetiredRoleName = 'Swankers Evals Runner'
)

$ErrorActionPreference = 'Stop'
$foundryUserRole = '53ca6127-db72-4b80-b1b0-d745d6d5456d' # Foundry User (formerly Azure AI User)
$openAiUserRole = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' # Cognitive Services OpenAI User

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
    # These dedicated CI applications must not retain older main/pull_request trust.
    # Client IDs are public identifiers; hiding them in secrets is not an access boundary.
    foreach ($credential in $existing) {
        if ($credential.subject -notin $Subjects.Values -or
            $credential.issuer -ne 'https://token.actions.githubusercontent.com' -or
            @($credential.audiences).Count -ne 1 -or
            $credential.audiences[0] -ne 'api://AzureADTokenExchange') {
            Write-Host "  removing obsolete credential $($credential.name)"
            Invoke-Az @('ad', 'app', 'federated-credential', 'delete', '--id', $App.id,
                '--federated-credential-id', $credential.id) | Out-Null
        }
    }
    $existing = (Invoke-Az @('ad', 'app', 'federated-credential', 'list', '--id', $App.id, '-o', 'json')) | ConvertFrom-Json
    foreach ($name in $Subjects.Keys) {
        if ($existing | Where-Object { $_.subject -eq $Subjects[$name] }) { Write-Host "  credential exists: $($Subjects[$name])"; continue }
        $file = New-TemporaryFile
        try {
            @{ name = $name; issuer = 'https://token.actions.githubusercontent.com'; subject = $Subjects[$name]; audiences = @('api://AzureADTokenExchange') } | ConvertTo-Json | Set-Content $file
            Write-Host "  adding credential $($Subjects[$name])"
            Invoke-Az @('ad', 'app', 'federated-credential', 'create', '--id', $App.id, '--parameters', "@$file") | Out-Null
        }
        finally { Remove-Item -LiteralPath $file -Force }
    }
}

$subscription = (Invoke-Az @('account', 'show', '--query', 'id', '-o', 'tsv')).Trim()
$tenant = (Invoke-Az @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Trim()
$accountScope = (Invoke-Az @('cognitiveservices', 'account', 'show', '-n', $FoundryAccount, '-g', $SharedResourceGroup, '--query', 'id', '-o', 'tsv')).Trim()
$repoInfo = gh api "repos/$Repo" --jq '{id: .id, ownerId: .owner.id, owner: .owner.login, name: .name}' | ConvertFrom-Json
$repoForms = @($Repo, "$($repoInfo.owner)@$($repoInfo.ownerId)/$($repoInfo.name)@$($repoInfo.id)")

# The evals project (infra/modules/foundry.bicep) is the only scope the evals identity gets.
$evalsProjectScope = "$accountScope/projects/$EvalsProject"
Invoke-Az @('resource', 'show', '--ids', $evalsProjectScope, '--query', 'name', '-o', 'tsv') | Out-Null

# Secure GitHub before enabling either environment's Azure trust.
& "$PSScriptRoot/github-security.ps1" -Repo $Repo -Environment $Environment -EvalsEnvironment $EvalsEnvironment

# Evals identity: only approved main jobs in its own environment; never PR subjects.
$evals = Ensure-App $EvalsAppName
$evalsSubjects = @{}
for ($i = 0; $i -lt $repoForms.Count; $i++) {
    $suffix = if ($i -eq 0) { '' } else { '-ids' }
    $evalsSubjects["github-env-$EvalsEnvironment$suffix"] = "repo:$($repoForms[$i]):environment:$EvalsEnvironment"
}
Ensure-FederatedCredentials $evals.App $evalsSubjects
Write-Host "Assigning Foundry User to $EvalsAppName on project $EvalsProject only"
Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $evals.Sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $foundryUserRole, '--scope', $evalsProjectScope, '-o', 'none') | Out-Null

# The cloud graders (FoundryEvals) call the judge model through the account's OpenAI endpoint as
# the caller, where a project-scoped role does not apply (CI run 36511188834: every grader item
# errored "Principal does not have access to API/Operation"). Cognitive Services OpenAI User at
# account scope covers those calls and nothing under AIServices/agents.
Write-Host "Assigning Cognitive Services OpenAI User to $EvalsAppName on $FoundryAccount (cloud graders)"
Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $evals.Sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $openAiUserRole, '--scope', $accountScope, '-o', 'none') | Out-Null

# Retire the account-scoped custom role from the first attempt (data-action exclusions cannot
# separate model calls from agent writes on the project Responses gateway).
$retiredRole = (Invoke-Az @('role', 'definition', 'list', '--name', $RetiredRoleName, '--custom-role-only', 'true', '--query', '[0].name', '-o', 'tsv')).Trim()
if ($retiredRole) {
    Write-Host "Removing retired role '$RetiredRoleName' and its assignments"
    & az role assignment delete --role $retiredRole --scope $accountScope -o none 2>$null
    Invoke-Az @('role', 'definition', 'delete', '--name', $retiredRole) | Out-Null
}

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

# Environments were protected above; never overwrite those rules with review-only settings.
gh secret set AZURE_CLIENT_ID --repo $Repo --env $EvalsEnvironment --body $evals.App.appId
if ($LASTEXITCODE -ne 0) { throw 'Could not set the eval environment client ID.' }
$repoSecrets = gh secret list --repo $Repo --json name | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not list repository secret names.' }
if ($repoSecrets.name -contains 'AZURE_CLIENT_ID') {
    gh secret delete AZURE_CLIENT_ID --repo $Repo
    if ($LASTEXITCODE -ne 0) { throw 'Could not remove the obsolete repository client ID.' }
}
gh secret set AZURE_TENANT_ID --repo $Repo --body $tenant
gh secret set AZURE_SUBSCRIPTION_ID --repo $Repo --body $subscription
gh secret set AZURE_DEPLOY_CLIENT_ID --repo $Repo --env $Environment --body $deploy.App.appId
$values = @{}
foreach ($line in (& azd env get-values)) { if ($line -match '^([^=]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"') } }
foreach ($key in 'FOUNDRY_PROJECT_ENDPOINT', 'AZURE_AI_MODEL_DEPLOYMENT_NAME', 'MCP_ENDPOINT', 'KEYVAULT_URI') {
    if (-not $values[$key]) { throw "azd environment has no $key; run azd up first." }
    gh variable set $key --repo $Repo --body $values[$key]
}
gh variable set FOUNDRY_EVALS_PROJECT_ENDPOINT --repo $Repo --body "https://$FoundryAccount.services.ai.azure.com/api/projects/$EvalsProject"

Write-Host "Done. Eval jobs log in as $EvalsAppName (project $EvalsProject only); the promote job logs in as $DeployAppName after approval in the '$Environment' environment."
