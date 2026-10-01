#!/usr/bin/env pwsh
<#
.SYNOPSIS
Puts the deployed demo into one stage configuration: routes the Coach endpoint by stage label,
resets SimLeague, seeds the scenario the stage needs, checks the deployed gate of the stage's own
credential, and says what the web header must show.

.DESCRIPTION
Presets (labels are the version metadata AgentDeploy stamps; the newest active version wins):

  thursday-good        v1-owner          hardened Coach, gpt-5.4; Thursday baseline, Friday "after"
  thursday-regressed   v2-owner          the "harmless tweak" that skips injury checks; rolled back live
  friday-before        v0-commissioner   naive prompt, gpt-4.1-mini, commissioner credential
                                         (DEMO: intentionally vulnerable); seeds the poisoned trade
  friday-contained     v0-owner          the same prompt and model with the owner credential, gate on
                                         (DEMO: the model still falls for the note; the gate and the
                                         scope hold); seeds the poisoned trade
  friday-after         v1-owner          seeds the poisoned trade for the same question, hardened

Routing takes seconds. Gates are provisioned, not routed, and each credential has its own: the
owner's gate is always on; the commissioner's is turned off for the whole Friday talk
(`azd env set MCP_COMMISSIONER_GATE_ENABLED false` and `azd provision`, about 2 minutes; back to
true afterwards). The script reads the deployed gates from /api/state and warns only when the gate
of the preset's own credential is wrong: commissioner off for friday-before, owner on for the other
four. Before, contained, and after are therefore three route changes and no provision.

.PARAMETER Preset
One of the presets above.

.PARAMETER Version
Route this version number instead of the preset's label (for example 4, the second v1-owner).

.PARAMETER SkipReset
Route only; leave the league as it is (the gate check still runs).

.EXAMPLE
pwsh demo/stage.ps1 -Preset friday-before
pwsh demo/stage.ps1 -Preset thursday-good -Version 4     # roll back to the older v1-owner
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('thursday-good', 'thursday-regressed', 'friday-before', 'friday-contained', 'friday-after')]
    [string] $Preset,
    [string] $Version,
    [switch] $SkipReset,
    [switch] $Local,
    [string] $BaseUrl,
    [string] $AdminKey
)

$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tools/Swankers.AgentDeploy/Swankers.AgentDeploy.csproj'
. (Join-Path $PSScriptRoot 'common.ps1')

# Run the built tool directly: `dotnet run` re-evaluates the build on every call, which costs a
# minute or more on this checkout; the stage switch must take seconds.
$tool = Join-Path $repo 'tools/Swankers.AgentDeploy/bin/Release/net10.0/Swankers.AgentDeploy.dll'
if (-not (Test-Path $tool)) {
    Write-Host "== building the deploy tool ==" -ForegroundColor Cyan
    & dotnet build $project -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'build failed.' }
}

$stages = @{
    'thursday-good'      = @{ Label = 'v1-owner';        Scenario = $null;            Credential = 'owner';        Header = 'prompt v1, owner credential, gate on' }
    'thursday-regressed' = @{ Label = 'v2-owner';        Scenario = $null;            Credential = 'owner';        Header = 'prompt v2, owner credential, gate on' }
    'friday-before'      = @{ Label = 'v0-commissioner'; Scenario = 'poisoned-trade'; Credential = 'commissioner'; Header = 'prompt v0, commissioner credential, gate OFF (red), model gpt-4.1-mini' }
    # DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
    'friday-contained'   = @{ Label = 'v0-owner';        Scenario = 'poisoned-trade'; Credential = 'owner';        Header = 'prompt v0, owner credential, gate on, model gpt-4.1-mini' }
    'friday-after'       = @{ Label = 'v1-owner';        Scenario = 'poisoned-trade'; Credential = 'owner';        Header = 'prompt v1, owner credential, gate on' }
}
$stage = $stages[$Preset]

Push-Location $repo
try {
    Write-Host "== route: $(if ($Version) { "v$Version" } else { $stage.Label }) ==" -ForegroundColor Cyan
    $routeArgs = if ($Version) { @('route', '--version', $Version) } else { @('route', '--label', $stage.Label) }
    $out = & dotnet $tool @routeArgs 2>&1 | ForEach-Object { "$_" }
    $out | Where-Object { $_ -notmatch '^\s*$' } | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'route failed.' }

    $api = Get-DemoApi -Repo $repo -Local:$Local -BaseUrl $BaseUrl -AdminKey $AdminKey
    if (-not $SkipReset) {
        Write-Host "== league ==" -ForegroundColor Cyan
        $resetArgs = @{ BaseUrl = $api.BaseUrl; AdminKey = $api.Headers['X-Demo-Admin-Key'] }
        if ($stage.Scenario) { $resetArgs.Scenario = $stage.Scenario }
        & (Join-Path $PSScriptRoot 'reset.ps1') @resetArgs
    }

    # The gate that matters is the one for the credential this stage's version holds.
    $gate = (Get-LeagueState -Api $api).gate
    if ($null -eq $gate) {
        Write-Warning 'The deployed MCP does not report its gates (older build); redeploy it.'
    }
    elseif ($stage.Credential -eq 'commissioner' -and $gate.commissionerGateEnabled) {
        Write-Warning "The commissioner gate is ON in the deployed MCP; '$Preset' needs it off. Run: azd env set MCP_COMMISSIONER_GATE_ENABLED false; azd provision (about 2 minutes)."
    }
    elseif ($stage.Credential -eq 'owner' -and -not $gate.ownerGateEnabled) {
        Write-Warning "The owner gate is OFF in the deployed MCP; '$Preset' needs it on. Fix the MCP configuration (Mcp:OwnerGateEnabled) and redeploy."
    }
    else {
        Write-Host "gate check: the $($stage.Credential) gate is $(if ($stage.Credential -eq 'commissioner') { 'off' } else { 'on' }), as '$Preset' needs."
    }

    $clock.Stop()
    Write-Host ""
    Write-Host ("{0} in {1:N0} s. Header should read: {2}." -f $Preset, $clock.Elapsed.TotalSeconds, $stage.Header) -ForegroundColor Green
}
finally {
    Pop-Location
}
