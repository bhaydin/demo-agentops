#!/usr/bin/env pwsh
<#
.SYNOPSIS
One-time setup of the GitHub Actions identity for evals.yml: an Entra application with GitHub
OIDC federated credentials (no secrets), the Foundry User role on the shared Foundry account,
and the repository secrets, variables, and protected environment the workflow reads.

.DESCRIPTION
Run once by the maintainer after `az login` (Owner on the subscription) and `gh auth login`.
Idempotent: existing app, service principal, credentials, and role assignment are reused.
The three "secrets" are identifiers, not credentials: OIDC issues short-lived tokens per job.

.PARAMETER Repo
GitHub repository, owner/name.

.PARAMETER Environment
GitHub environment that guards the promote job (a required reviewer is added: you).
#>
[CmdletBinding()]
param(
    [string] $Repo = 'bhaydin/demo-agentops',
    [string] $AppName = 'swankers-ci',
    [string] $SharedResourceGroup = 'rg-swankers-shared',
    [string] $FoundryAccount = 'foundry-swankers-vxzd',
    [string] $Environment = 'foundry'
)

$ErrorActionPreference = 'Stop'
$foundryUserRole = '53ca6127-db72-4b80-b1b0-d745d6d5456d' # Foundry User (formerly Azure AI User)

function Invoke-Az { param([string[]] $AzArgs) $out = & az @AzArgs 2>&1; if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs[0..1] -join ' ') failed: $out" }; return $out }

$subscription = (Invoke-Az @('account', 'show', '--query', 'id', '-o', 'tsv')).Trim()
$tenant = (Invoke-Az @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Trim()

# Application + service principal
$app = (Invoke-Az @('ad', 'app', 'list', '--display-name', $AppName, '--query', '[0]', '-o', 'json')) | ConvertFrom-Json
if (-not $app) {
    Write-Host "Creating application $AppName"
    $app = (Invoke-Az @('ad', 'app', 'create', '--display-name', $AppName, '-o', 'json')) | ConvertFrom-Json
}
$sp = (Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$($app.appId)'", '--query', '[0]', '-o', 'json')) | ConvertFrom-Json
if (-not $sp) {
    Write-Host "Creating service principal for $AppName"
    $sp = (Invoke-Az @('ad', 'sp', 'create', '--id', $app.appId, '-o', 'json')) | ConvertFrom-Json
}
Write-Host "Application $($app.appId), service principal $($sp.id)"

# Federated credentials: main branch, pull requests, and the protected environment. GitHub's
# OIDC subject can carry the owner and repository ids ("repo:owner@123/name@456:..."), so both
# the classic and the id-qualified subject are registered.
$repoInfo = gh api "repos/$Repo" --jq '{id: .id, ownerId: .owner.id, owner: .owner.login, name: .name}' | ConvertFrom-Json
$repoWithIds = "$($repoInfo.owner)@$($repoInfo.ownerId)/$($repoInfo.name)@$($repoInfo.id)"
$existing = (Invoke-Az @('ad', 'app', 'federated-credential', 'list', '--id', $app.id, '-o', 'json')) | ConvertFrom-Json
$subjects = @{}
foreach ($form in @(@{ Suffix = ''; Repo = $Repo }, @{ Suffix = '-ids'; Repo = $repoWithIds })) {
    $subjects["github-main$($form.Suffix)"] = "repo:$($form.Repo):ref:refs/heads/main"
    $subjects["github-pull-request$($form.Suffix)"] = "repo:$($form.Repo):pull_request"
    $subjects["github-env-$Environment$($form.Suffix)"] = "repo:$($form.Repo):environment:$Environment"
}
foreach ($name in $subjects.Keys) {
    if ($existing | Where-Object { $_.subject -eq $subjects[$name] }) { Write-Host "Federated credential exists: $($subjects[$name])"; continue }
    $file = New-TemporaryFile
    @{ name = $name; issuer = 'https://token.actions.githubusercontent.com'; subject = $subjects[$name]; audiences = @('api://AzureADTokenExchange') } | ConvertTo-Json | Set-Content $file
    Write-Host "Adding federated credential $($subjects[$name])"
    Invoke-Az @('ad', 'app', 'federated-credential', 'create', '--id', $app.id, '--parameters', "@$file") | Out-Null
    Remove-Item $file -Force
}

# Foundry User on the shared account: model inference, cloud evals, hosted-agent versions and routing.
$scope = (Invoke-Az @('cognitiveservices', 'account', 'show', '-n', $FoundryAccount, '-g', $SharedResourceGroup, '--query', 'id', '-o', 'tsv')).Trim()
Write-Host "Assigning Foundry User on $FoundryAccount"
Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $sp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $foundryUserRole, '--scope', $scope, '-o', 'none') | Out-Null

# Repository secrets (identifiers) and variables (endpoints from the azd environment).
gh secret set AZURE_CLIENT_ID --repo $Repo --body $app.appId
gh secret set AZURE_TENANT_ID --repo $Repo --body $tenant
gh secret set AZURE_SUBSCRIPTION_ID --repo $Repo --body $subscription
$values = @{}
foreach ($line in (& azd env get-values)) { if ($line -match '^([^=]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"') } }
foreach ($key in 'FOUNDRY_PROJECT_ENDPOINT', 'AZURE_AI_MODEL_DEPLOYMENT_NAME', 'MCP_ENDPOINT', 'KEYVAULT_URI') {
    if (-not $values[$key]) { throw "azd environment has no $key; run azd up first." }
    gh variable set $key --repo $Repo --body $values[$key]
}

# Protected environment: the promote job waits for your approval.
$me = gh api user --jq .id
$body = New-TemporaryFile
@{ reviewers = @(@{ type = 'User'; id = [int]$me }) } | ConvertTo-Json -Depth 3 | Set-Content $body
gh api -X PUT "repos/$Repo/environments/$Environment" --input $body | Out-Null
Remove-Item $body -Force

Write-Host "Done. evals.yml can now log in as $AppName; the '$Environment' environment requires your approval to promote."
