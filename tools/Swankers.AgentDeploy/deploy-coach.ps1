#!/usr/bin/env pwsh
<#
.SYNOPSIS
Deploys the three stage versions of the Coach hosted agent to the shared Foundry project and
routes the endpoint at v1-owner (the hardened default).

.DESCRIPTION
Run after `azd up` from the repo root. Reads the azd environment (FOUNDRY_PROJECT_ENDPOINT,
KEYVAULT_URI, MCP_ENDPOINT, AZURE_AI_MODEL_DEPLOYMENT_NAME), publishes Swankers.Coach once,
uploads it three times with different non-secret settings (prompt version, credential key),
then records the agent identity in the azd environment (COACH_AGENT_PRINCIPAL_ID) and
re-provisions so that identity can read the MCP credential from Key Vault.

.PARAMETER SkipPublish
Reuse the publish output (in the temp folder) from a previous run.

.PARAMETER SkipProvision
Do not run `azd provision` for the Key Vault role assignment; print the command instead.

.PARAMETER BeforeModel
Chat deployment for the Friday "before" version (v0-commissioner). gpt-5.4 refuses injected
tool output whatever the prompt, so the vulnerable configuration runs a weaker model that the
shared Foundry module deploys alongside (infra/modules/foundry.bicep, extraModels).
#>
[CmdletBinding()]
param(
    [string] $AgentName = 'Coach',
    # Must match the extraModels default in infra/modules/foundry.bicep (a test checks the two agree).
    [string] $BeforeModel = 'gpt-4.1-mini',
    [switch] $SkipPublish,
    [switch] $SkipProvision
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tool = Join-Path $repo 'tools/Swankers.AgentDeploy/Swankers.AgentDeploy.csproj'
# Outside the checkout: `dotnet publish -o` cannot take a path containing ',' or ';' (MSB1006).
$publishDir = Join-Path ([IO.Path]::GetTempPath()) 'swankers-coach-publish'

function Invoke-Tool {
    param([string[]] $ToolArgs)
    $output = & dotnet run --project $tool -c Release -- @ToolArgs 2>&1 | ForEach-Object { "$_" }
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Swankers.AgentDeploy $($ToolArgs[0]) failed." }
    return $output
}

function Get-Value {
    param([string[]] $Lines, [string] $Key)
    $line = $Lines | Where-Object { $_ -like "$Key=*" } | Select-Object -Last 1
    if (-not $line) { throw "Tool output did not include $Key=" }
    return $line.Substring($Key.Length + 1).Trim()
}

Push-Location $repo
try {
    # azd environment -> process environment, only the keys the tool reads.
    $values = @{}
    foreach ($line in (& azd env get-values)) {
        if ($line -match '^([^=]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"') }
    }
    foreach ($key in 'FOUNDRY_PROJECT_ENDPOINT', 'KEYVAULT_URI', 'MCP_ENDPOINT', 'AZURE_AI_MODEL_DEPLOYMENT_NAME') {
        if (-not $values[$key]) { throw "azd environment has no $key. Run 'azd up' first." }
        Set-Item -Path "env:$key" -Value $values[$key]
    }

    if (-not $SkipPublish) { Invoke-Tool @('publish', '--output', $publishDir) | Out-Null }

    $stages = @(
        @{ Label = 'v1-owner';        Prompt = 'v1'; Credential = 'Mcp:OwnerCredential' }
        @{ Label = 'v2-owner';        Prompt = 'v2'; Credential = 'Mcp:OwnerCredential' }
        # DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
        @{ Label = 'v0-commissioner'; Prompt = 'v0'; Credential = 'Mcp:CommissionerCredential'; Model = $BeforeModel }
    )

    $versions = @{}
    foreach ($stage in $stages) {
        Write-Host "`n== $($stage.Label) ==" -ForegroundColor Cyan
        $toolArgs = @('create', '--name', $AgentName, '--label', $stage.Label, '--prompt', $stage.Prompt,
            '--credential-key', $stage.Credential, '--publish-dir', $publishDir)
        if ($stage.Model) { $toolArgs += @('--model', $stage.Model) }
        $out = Invoke-Tool $toolArgs
        $versions[$stage.Label] = Get-Value $out 'version'
    }

    Write-Host "`n== route -> v1-owner ==" -ForegroundColor Cyan
    Invoke-Tool @('route', '--name', $AgentName, '--version', $versions['v1-owner']) | Out-Null

    Write-Host "`n== agent identity ==" -ForegroundColor Cyan
    $principalId = Get-Value (Invoke-Tool @('identity', '--name', $AgentName)) 'principalId'
    & azd env set COACH_AGENT_PRINCIPAL_ID $principalId
    if ($LASTEXITCODE -ne 0) { throw 'azd env set failed.' }

    if ($SkipProvision) {
        Write-Host "Now run 'azd provision' so the agent identity can read the MCP credential from Key Vault."
    }
    else {
        Write-Host "`n== azd provision (Key Vault role for the agent identity) ==" -ForegroundColor Cyan
        & azd provision --no-prompt
        if ($LASTEXITCODE -ne 0) { throw 'azd provision failed.' }
    }

    Write-Host "`nCoach versions: " -NoNewline
    Write-Host (($versions.GetEnumerator() | Sort-Object Value | ForEach-Object { "$($_.Key)=v$($_.Value)" }) -join ', ')
    Write-Host "Routed: v$($versions['v1-owner']) (v1-owner)."
    Write-Host "Rollback/switch (from the repo root; the tool reads the azd environment, or pass the endpoint):"
    Write-Host "  dotnet run --project tools/Swankers.AgentDeploy -- route --version <n> --project-endpoint $env:FOUNDRY_PROJECT_ENDPOINT"
}
finally {
    Pop-Location
}
