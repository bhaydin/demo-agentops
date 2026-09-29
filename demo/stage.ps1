#!/usr/bin/env pwsh
<#
.SYNOPSIS
Puts the deployed demo into one stage configuration: routes the Coach endpoint by stage label,
resets SimLeague, seeds the scenario the stage needs, and says what the web header must show.

.DESCRIPTION
Presets (labels are the version metadata AgentDeploy stamps; the newest version of a label wins):

  thursday-good        v1-owner          hardened Coach, gpt-5.4; Thursday baseline, Friday "after"
  thursday-regressed   v2-owner          the "harmless tweak" that skips injury checks; rolled back live
  friday-before        v0-commissioner   naive prompt, gpt-4.1-mini, commissioner credential
                                         (DEMO: intentionally vulnerable); seeds the poisoned trade
  friday-after         v1-owner          seeds the poisoned trade for the same question, hardened

Routing takes seconds. The commissioner gate flag is provisioned, not routed: set
MCP_COMMISSIONER_GATE_ENABLED=false and run `azd provision` before the Friday talk, and set it
back to true afterwards; this script only warns when the flag does not match the preset. The
owner credential's gate is always on, so friday-after needs no provisioning.

.PARAMETER Preset
One of the presets above.

.PARAMETER Version
Route this version number instead of the preset's label (for example 4, the second v1-owner).

.PARAMETER SkipReset
Route only; leave the league as it is.

.EXAMPLE
pwsh demo/stage.ps1 -Preset friday-before
pwsh demo/stage.ps1 -Preset thursday-good -Version 4     # roll back to the older v1-owner
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('thursday-good', 'thursday-regressed', 'friday-before', 'friday-after')]
    [string] $Preset,
    [string] $Version,
    [switch] $SkipReset
)

$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tools/Swankers.AgentDeploy/Swankers.AgentDeploy.csproj'
# Run the built tool directly: `dotnet run` re-evaluates the build on every call, which costs a
# minute or more on this checkout; the stage switch must take seconds.
$tool = Join-Path $repo 'tools/Swankers.AgentDeploy/bin/Release/net10.0/Swankers.AgentDeploy.dll'
if (-not (Test-Path $tool)) {
    Write-Host "== building the deploy tool ==" -ForegroundColor Cyan
    & dotnet build $project -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'build failed.' }
}

$stages = @{
    'thursday-good'      = @{ Label = 'v1-owner';        Scenario = $null;             Gate = 'true';  Header = 'prompt v1, owner credential, gate on' }
    'thursday-regressed' = @{ Label = 'v2-owner';        Scenario = $null;             Gate = 'true';  Header = 'prompt v2, owner credential, gate on' }
    'friday-before'      = @{ Label = 'v0-commissioner'; Scenario = 'poisoned-trade';  Gate = 'false'; Header = 'prompt v0, commissioner credential, gate OFF (red), model gpt-4.1-mini' }
    'friday-after'       = @{ Label = 'v1-owner';        Scenario = 'poisoned-trade';  Gate = 'true';  Header = 'prompt v1, owner credential, gate on' }
}
$stage = $stages[$Preset]

Push-Location $repo
try {
    Write-Host "== route: $(if ($Version) { "v$Version" } else { $stage.Label }) ==" -ForegroundColor Cyan
    $routeArgs = if ($Version) { @('route', '--version', $Version) } else { @('route', '--label', $stage.Label) }
    $out = & dotnet $tool @routeArgs 2>&1 | ForEach-Object { "$_" }
    $out | Where-Object { $_ -notmatch '^\s*$' } | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'route failed.' }

    if (-not $SkipReset) {
        Write-Host "== league ==" -ForegroundColor Cyan
        $resetArgs = @{}
        if ($stage.Scenario) { $resetArgs.Scenario = $stage.Scenario }
        & (Join-Path $PSScriptRoot 'reset.ps1') @resetArgs
    }

    $gateLine = (& azd env get-values 2>$null) | Where-Object { $_ -like 'MCP_COMMISSIONER_GATE_ENABLED=*' } | Select-Object -Last 1
    $gate = if ($gateLine) { $gateLine.Substring('MCP_COMMISSIONER_GATE_ENABLED='.Length).Trim('"') } else { 'true (default)' }
    if ($gate -ne $stage.Gate -and -not ($gate -like 'true*' -and $stage.Gate -eq 'true')) {
        Write-Warning "MCP_COMMISSIONER_GATE_ENABLED is '$gate' in the azd environment but '$Preset' expects '$($stage.Gate)'. Set it and run 'azd provision' (about 2 minutes) before the talk."
    }

    $clock.Stop()
    Write-Host ""
    Write-Host ("{0} in {1:N0} s. Header should read: {2}." -f $Preset, $clock.Elapsed.TotalSeconds, $stage.Header) -ForegroundColor Green
}
finally {
    Pop-Location
}
