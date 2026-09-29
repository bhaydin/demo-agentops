#!/usr/bin/env pwsh
<#
.SYNOPSIS
Restores SimLeague to the snapshot and, optionally, seeds a demo scenario. Under 10 seconds.

.DESCRIPTION
Calls the MCP server's demo REST API (POST /api/admin/reset, then POST /api/admin/seed/{scenario})
with the demo admin key. Targets the deployed MCP server by default (MCP_BASE_URL from the selected
azd environment, the key from Key Vault after `az login`), or a local one with -Local. The reset
also cancels pending confirmations and drains an in-flight approval, so the approval dialog closes.

.PARAMETER Scenario
A file name under data/demo without .json, for example poisoned-trade. Omit to reset only.

.PARAMETER Local
Target http://localhost:5210 and read the admin key from MCP_DEMO_ADMIN_KEY (or -AdminKey).

.PARAMETER BaseUrl
Override the MCP base URL (no trailing /api).

.PARAMETER AdminKey
Override the demo admin key (otherwise MCP_DEMO_ADMIN_KEY, then Key Vault).

.EXAMPLE
pwsh demo/reset.ps1
pwsh demo/reset.ps1 -Scenario poisoned-trade
pwsh demo/reset.ps1 -Local -Scenario poisoned-trade
#>
[CmdletBinding()]
param(
    [string] $Scenario,
    [switch] $Local,
    [string] $BaseUrl,
    [string] $AdminKey,
    [string] $VaultName = 'kv-swankers-vxzd',
    [string] $KeySecretName = 'Mcp--DemoAdminKey'
)

$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path -Parent $PSScriptRoot

if (-not $BaseUrl) {
    if ($Local) {
        $BaseUrl = 'http://localhost:5210'
    }
    else {
        $line = (& azd env get-values --cwd $repo 2>$null) | Where-Object { $_ -like 'MCP_BASE_URL=*' } | Select-Object -Last 1
        if (-not $line) { throw 'No MCP_BASE_URL in the selected azd environment; pass -BaseUrl or -Local.' }
        $BaseUrl = $line.Substring('MCP_BASE_URL='.Length).Trim('"')
    }
}
$BaseUrl = $BaseUrl.TrimEnd('/')

if (-not $AdminKey) { $AdminKey = $env:MCP_DEMO_ADMIN_KEY }
if (-not $AdminKey -and -not $Local) {
    $AdminKey = & az keyvault secret show --vault-name $VaultName --name $KeySecretName --query value -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $AdminKey) { throw "Could not read $KeySecretName from $VaultName; pass -AdminKey." }
}
if (-not $AdminKey) { throw 'No admin key: set MCP_DEMO_ADMIN_KEY or pass -AdminKey.' }

$headers = @{ 'X-Demo-Admin-Key' = $AdminKey }

$reset = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/admin/reset" -Headers $headers -TimeoutSec 20
Write-Host "reset: $($reset.status), canceled confirmations: $($reset.canceledConfirmations)"

if ($Scenario) {
    $seed = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/admin/seed/$Scenario" -Headers $headers -TimeoutSec 20
    $trade = $seed.trade
    if ($trade) {
        Write-Host "seeded $($seed.scenario): trade $($trade.id) from $($trade.fromFranchiseName) to $($trade.toFranchiseName) ($($trade.give.name -join ', ') for $($trade.get.name -join ', '))"
    }
    else {
        Write-Host "seeded $Scenario"
    }
}

$clock.Stop()
Write-Host ("done in {0:N1} s against {1}" -f $clock.Elapsed.TotalSeconds, $BaseUrl)
if ($clock.Elapsed.TotalSeconds -gt 10) { Write-Warning 'Slower than the 10 s target.' }
