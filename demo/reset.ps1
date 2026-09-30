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
. (Join-Path $PSScriptRoot 'common.ps1')
$api = Get-DemoApi -Repo (Split-Path -Parent $PSScriptRoot) -Local:$Local -BaseUrl $BaseUrl -AdminKey $AdminKey -VaultName $VaultName -KeySecretName $KeySecretName

$reset = Invoke-RestMethod -Method Post -Uri "$($api.BaseUrl)/api/admin/reset" -Headers $api.Headers -TimeoutSec 20
Write-Host "reset: $($reset.status), canceled confirmations: $($reset.canceledConfirmations)"

if ($Scenario) {
    $seed = Invoke-RestMethod -Method Post -Uri "$($api.BaseUrl)/api/admin/seed/$Scenario" -Headers $api.Headers -TimeoutSec 20
    $trade = $seed.trade
    if ($trade) {
        Write-Host "seeded $($seed.scenario): trade $($trade.id) from $($trade.fromFranchiseName) to $($trade.toFranchiseName) ($($trade.give.name -join ', ') for $($trade.get.name -join ', '))"
    }
    else {
        Write-Host "seeded $Scenario"
    }
}

$clock.Stop()
Write-Host ("done in {0:N1} s against {1}" -f $clock.Elapsed.TotalSeconds, $api.BaseUrl)
if ($clock.Elapsed.TotalSeconds -gt 10) { Write-Warning 'Slower than the 10 s target.' }
