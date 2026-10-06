#!/usr/bin/env pwsh
<#
.SYNOPSIS
Stage commands for the WI.NET WorkplaceMeetingPrep demonstration (demo/WinetRunbook.md). Each
action is one step of the runbook; nothing here changes the product, the prompts, or the evaluation.

.DESCRIPTION
  preflight   checks the build, the frozen hashes, Azure sign-in, the Aspire CLI, ports, and leftovers
  reset       removes the workplace Responses state roots (football state is untouched)
  dashboard   runs the Aspire dashboard in this terminal (frontend :18888, OTLP/HTTP :4318)
  coach       runs the workplace Coach in this terminal, Replay by default, tracing to the dashboard
  ask         sends the demo query to the running Coach, prints the answer and the trace id
  eval        runs the workplace evaluation for one explicit prompt version and labels the report
  show        prints the stage view of a report: gate, failing checks, the E7 bullet, the evidence
  diff        prints the one rule that differs between meetingprep-v1.1 and meetingprep-v2

The prompt version is always explicit. There is no action that runs the evaluation on a default.

.EXAMPLE
pwsh demo/winet.ps1 preflight
pwsh demo/winet.ps1 eval -Version v1.1 -Run control
pwsh demo/winet.ps1 eval -Version v2 -Run regression
pwsh demo/winet.ps1 show -Version v2
pwsh demo/winet.ps1 eval -Version v1.1 -Run recovery
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('preflight', 'reset', 'dashboard', 'coach', 'ask', 'eval', 'show', 'diff')]
    [string] $Action,
    [ValidateSet('v1', 'v1.1', 'v2')]
    [string] $Version,
    [string] $Run,
    [string] $Report,
    [string] $Query = 'Before my client meeting, summarize the latest confirmed decisions, open commitments and unresolved risks, with supporting sources. Explicitly identify where evidence is missing or contradictory.',
    [switch] $Live,
    [string] $Endpoint = $(if ($env:FOUNDRY_PROJECT_ENDPOINT) { $env:FOUNDRY_PROJECT_ENDPOINT } else { 'https://foundry-swankers-vxzd.services.ai.azure.com/api/projects/swankers-coach' }),
    [int] $CoachPort = 8088,
    [int] $DashboardPort = 18888,
    [int] $OtlpPort = 4318
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$reports = Join-Path $repo 'artifacts/evals/workplace'

# Frozen at W3/W4 (docs/BUILD-PLAN.md). Hashes are of the file with line endings normalized to LF.
$frozen = [ordered]@{
    'src/Swankers.Coach/prompts/meetingprep-v1.1.md'            = '5A57E9C358E800B6A34CD23862E850D8DE5B7EA8939970FA7B16AB3E8ACAD569'
    'src/Swankers.Coach/prompts/meetingprep-v2.md'              = '1A25C091C632117DAD06FBD770188638A2ADD6459D78EF16D39CDB02354DFAA5'
    'data/workplace/northwind-v1.evidence.json'                 = '72FE0287021C354E7FE3C64BF53E6055289887965BFF0660DB264790582028CD'
    'tests/Swankers.Evals/workplace-golden/meeting-prep.jsonl'  = 'B33E5C326F201FFF00332716503C9A38D56B0FC936F89E1844E304284BAB1615'
    'tests/Swankers.Evals/workplace-golden/northwind-v1.key.json' = '40A5B8BE3B725B9FA7922F49F6427DBE04EAA8167305E8E8D0F5B79DFEF39845'
    'tests/Swankers.Evals/workplace-evalsettings.json'          = 'FFCCDE250F7571C5E95CD280E8B11223F48DCCBC8E8662E347C634F6BC4BF799'
}

function Write-Step([string] $text) { Write-Host "== $text ==" -ForegroundColor Cyan }
function Write-Ok([string] $text) { Write-Host "  ok    $text" -ForegroundColor Green }
function Write-Bad([string] $text) { Write-Host "  FAIL  $text" -ForegroundColor Red }
function Write-Note([string] $text) { Write-Host "  note  $text" -ForegroundColor Yellow }

function Get-LfHash([string] $relative) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $relative)).Replace("`r`n", "`n")
    return [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '')
}

function Get-LatestReport([string] $version, [string] $run) {
    $pattern = if ($run) { "meetingprep-$version-$run-*.json" } else { "meetingprep-$version-*.json" }
    return Get-ChildItem (Join-Path $reports $pattern) -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike 'latest-*' } | Sort-Object LastWriteTime | Select-Object -Last 1
}

function Test-PortFree([int] $port) {
    try { $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port); $listener.Start(); $listener.Stop(); return $true } catch { return $false }
}

function Get-FixtureItem([string] $id) {
    $fixture = Get-Content (Join-Path $repo 'data/workplace/northwind-v1.evidence.json') -Raw | ConvertFrom-Json
    return $fixture.items | Where-Object { $_.id -eq $id }
}

function Show-Report([IO.FileInfo] $file) {
    $j = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $p = $j.provenance
    Write-Host ""
    Write-Host ("{0}  {1}  ({2}, {3} UTC)" -f $p.promptId, $j.status.ToUpperInvariant(), $p.runLabel, $p.timestampUtc) -ForegroundColor $(if ($j.status -eq 'pass') { 'Green' } else { 'Red' })
    Write-Host ("  deterministic {0}/{1} (100% needed)   judged {2}/{3} (80% needed)   cases {4}/{5}" -f $j.deterministic.passed, $j.deterministic.total, $j.judged.passed, $j.judged.total, $j.casesPassed, $j.casesTotal)
    Write-Host ("  prompt sha256 {0}…  judge {1}  fixture {2}  code {3}{4}" -f $p.promptSha256.Substring(0, 8), $p.judgeConfig, $p.fixture, $p.gitCommit.Substring(0, 7), $(if ($p.gitDirty) { ' (dirty)' } else { '' }))
    Write-Host ("  mode {0}; every case: tools {1}" -f $p.mode.ToUpperInvariant(), (($j.cases | ForEach-Object { $_.tools -join ',' } | Select-Object -Unique) -join ' | '))
    foreach ($c in $j.cases) {
        $mark = if ($c.incomplete) { 'INCOMPLETE' } elseif ($c.passed) { 'pass' } else { 'FAIL' }
        Write-Host ("  {0}  {1,-10} {2}" -f $c.id, $mark, $(if ($c.passed) { '' } else { ($c.failures -join ' | ') })) -ForegroundColor $(if ($c.passed) { 'Gray' } else { 'Red' })
    }
    foreach ($c in ($j.cases | Where-Object { -not $_.passed })) {
        $section = ''
        $hits = @()
        foreach ($line in ($c.answer -split "`n")) {
            if ($line -match '^## (.+)$') { $section = $Matches[1].Trim() }
            elseif ($line -match '\[E7\]' -and $section) { $hits += [pscustomobject]@{ Section = $section; Line = $line.Trim() } }
        }
        if ($hits.Count -gt 0) {
            Write-Host ""
            Write-Host ("  {0}: where the answer put E7" -f $c.id) -ForegroundColor Yellow
            foreach ($h in $hits) { Write-Host ("    under '## {0}':" -f $h.Section) -ForegroundColor $(if ($h.Section -eq 'Confirmed decisions') { 'Red' } else { 'Gray' }); Write-Host ("      {0}" -f $h.Line) }
        }
    }
    $e7 = Get-FixtureItem 'E7'
    Write-Host ""
    Write-Host "  evidence E7 as the tool returned it:" -ForegroundColor Yellow
    Write-Host ("    {0}" -f ($e7.text -replace "`n", ' '))
    Write-Host ("  report: {0}" -f $file.FullName)
}

function Show-Diff {
    $v11 = [IO.File]::ReadAllText((Join-Path $repo 'src/Swankers.Coach/prompts/meetingprep-v1.1.md')).Replace("`r`n", "`n").Split("`n")
    $v2 = [IO.File]::ReadAllText((Join-Path $repo 'src/Swankers.Coach/prompts/meetingprep-v2.md')).Replace("`r`n", "`n").Split("`n")
    $removed = $v11 | Where-Object { $_ -notin $v2 -and $_ -like '- *' }
    $added = $v2 | Where-Object { $_ -notin $v11 -and $_ -like '- *' }
    Write-Host ""
    Write-Host "  meetingprep-v1.1 (baseline):" -ForegroundColor Green
    $removed | ForEach-Object { Write-Host "    $_" }
    Write-Host ""
    Write-Host "  meetingprep-v2 (the change under test):" -ForegroundColor Red
    $added | ForEach-Object { Write-Host "    $_" }
    Write-Host ""
    Write-Host ("  {0} line(s) removed, {1} added; the title line also changes. Nothing else differs (a test asserts it)." -f @($removed).Count, @($added).Count)
}

Push-Location $repo
try {
    switch ($Action) {
        'preflight' {
            Write-Step 'toolchain'
            $sdk = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
            $have = (& dotnet --version).Trim()
            if ($have -eq $sdk) { Write-Ok "dotnet SDK $have (global.json)" } else { Write-Bad "dotnet SDK $have, global.json wants $sdk" }
            foreach ($dll in 'src/Swankers.Coach/bin/Release/net10.0/Swankers.Coach.dll', 'tests/Swankers.Evals/bin/Release/net10.0/Swankers.Evals.dll') {
                if (Test-Path (Join-Path $repo $dll)) { Write-Ok "built: $dll ($((Get-Item (Join-Path $repo $dll)).LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" } else { Write-Bad "missing $dll; run: dotnet build SwankersCoach.slnx -c Release" }
            }
            $aspire = Get-Command aspire -ErrorAction SilentlyContinue
            if ($aspire) { Write-Ok ("aspire CLI " + ((& aspire --version 2>$null | Select-Object -First 1) -replace '\+.*$', '')) } else { Write-Bad 'aspire CLI not found (dotnet tool install -g aspire.cli)' }

            Write-Step 'code and frozen inputs'
            $head = (& git rev-parse --short HEAD).Trim()
            $dirty = (& git status --porcelain)
            if ($dirty) { Write-Note "HEAD $head with uncommitted changes:`n$($dirty -join "`n")" } else { Write-Ok "HEAD $head, clean tree" }
            foreach ($entry in $frozen.GetEnumerator()) {
                $actual = Get-LfHash $entry.Key
                if ($actual -eq $entry.Value) { Write-Ok ("{0}  {1}…" -f $entry.Key, $actual.Substring(0, 8)) } else { Write-Bad ("{0} changed: {1}… (frozen {2}…)" -f $entry.Key, $actual.Substring(0, 8), $entry.Value.Substring(0, 8)) }
            }

            Write-Step 'Azure and Foundry'
            $sub = & az account show --query name -o tsv 2>$null
            if ($LASTEXITCODE -eq 0 -and $sub) { Write-Ok "az signed in (subscription: $sub)" } else { Write-Bad 'az not signed in: run az login' }
            $exp = & az account get-access-token --resource https://ai.azure.com --query expiresOn -o tsv 2>$null
            if ($LASTEXITCODE -eq 0 -and $exp) { Write-Ok "Foundry token available (expires $exp)" } else { Write-Bad 'no token for https://ai.azure.com: run az login' }
            Write-Ok "project endpoint: $Endpoint"

            Write-Step 'environment'
            foreach ($name in 'WORKPLACE_EVAL', 'WORKPLACE_PROMPT_VERSION', 'WORKPLACE_EVAL_RUN', 'WORKPLACE_EVAL_MODEL', 'WORKIQ_LIVE', 'WORKPLACE_JUDGE_CALIBRATION') {
                if ([Environment]::GetEnvironmentVariable($name)) { Write-Note "$name is set in this shell; the eval action sets its own values and restores these afterwards" }
            }
            if ($env:APPLICATIONINSIGHTS_CONNECTION_STRING) { Write-Note 'APPLICATIONINSIGHTS_CONNECTION_STRING is set machine-wide; the workplace Coach drops it (local traces go to the dashboard only)' }
            foreach ($port in $DashboardPort, $OtlpPort, $CoachPort) { if (Test-PortFree $port) { Write-Ok "port $port free" } else { Write-Note "port $port in use (dashboard or Coach already running?)" } }

            Write-Step 'state and reports'
            $roots = Get-ChildItem (Join-Path $env:USERPROFILE '.agentserver/scenarios') -Directory -Filter 'workplace-*' -ErrorAction SilentlyContinue
            if ($roots) { Write-Note ("workplace state present: {0}  (pwsh demo/winet.ps1 reset clears it)" -f (($roots | ForEach-Object { $_.Name }) -join ', ')) } else { Write-Ok 'no workplace state roots (fresh)' }
            foreach ($v in 'v1.1', 'v2') {
                $latest = Get-LatestReport $v
                if ($latest) { $s = (Get-Content $latest.FullName -Raw | ConvertFrom-Json).status; Write-Ok ("latest {0} report: {1} ({2})" -f $v, $latest.Name, $s.ToUpperInvariant()) } else { Write-Note "no $v report yet under artifacts/evals/workplace" }
            }
        }

        'reset' {
            Write-Step 'workplace state'
            $base = Join-Path $env:USERPROFILE '.agentserver/scenarios'
            $roots = Get-ChildItem $base -Directory -Filter 'workplace-*' -ErrorAction SilentlyContinue
            foreach ($root in $roots) { [IO.Directory]::Delete($root.FullName, $true); Write-Ok "removed $($root.FullName)" }
            if (-not $roots) { Write-Ok "nothing to remove under $base" }
            New-Item -ItemType Directory -Force $reports | Out-Null
            Write-Ok "reports folder: $reports (existing reports kept)"
            Write-Ok 'football state (~/.agentserver/responses) untouched'
        }

        'dashboard' {
            Write-Step "Aspire dashboard: http://localhost:$DashboardPort  (OTLP/HTTP http://localhost:$OtlpPort)"
            & aspire dashboard run --frontend-url "http://localhost:$DashboardPort" --otlp-http-url "http://localhost:$OtlpPort" --allow-anonymous --nologo
        }

        'coach' {
            if (-not $Version) { throw 'coach needs -Version (v1.1 is the baseline; v2 is the regression). The version is always explicit on stage.' }
            $mode = if ($Live) { 'Live' } else { 'Replay' }
            $arguments = @('run', '--project', 'src/Swankers.Coach', '-c', 'Release', '--no-build', '--', '--Coach:Scenario', 'workplace', '--Workplace:Mode', $mode, '--Coach:PromptVersion', $Version, '--Coach:ProjectEndpoint', $Endpoint)
            if ($Live) {
                if (-not $env:WORKIQ_ACCOUNT) { throw 'Live needs WORKIQ_ACCOUNT (the signed-in Work IQ account) in this shell; it is passed to the CLI and never written to a file.' }
                $arguments += @('--Workplace:WorkIq:Account', $env:WORKIQ_ACCOUNT)
            }
            $env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:$OtlpPort"
            $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'http/protobuf'
            $env:PORT = "$CoachPort"
            Write-Step "workplace Coach, $mode, meetingprep-$Version, traces to http://localhost:$OtlpPort"
            Write-Host ("  dotnet " + (($arguments | ForEach-Object { if ($_ -eq $env:WORKIQ_ACCOUNT) { '<account>' } else { $_ } }) -join ' ')) -ForegroundColor Gray
            & dotnet @arguments
        }

        'ask' {
            $traceId = -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
            $spanId = -join ((1..16) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
            Write-Step "POST http://localhost:$CoachPort/responses  (trace id $traceId)"
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-RestMethod -Method Post -Uri "http://localhost:$CoachPort/responses" -ContentType 'application/json' -Body (@{ input = $Query } | ConvertTo-Json -Compress) -Headers @{ traceparent = "00-$traceId-$spanId-01" } -TimeoutSec 180
            $clock.Stop()
            $text = ($response.output | Where-Object { $_.type -eq 'message' } | ForEach-Object { $_.content | Where-Object { $_.type -eq 'output_text' } | ForEach-Object { $_.text } }) -join "`n"
            Write-Host ""
            Write-Host $text
            Write-Host ""
            Write-Host ("answered in {0:N0} s. In the dashboard: Traces, resource Swankers.Coach, trace {1}." -f $clock.Elapsed.TotalSeconds, $traceId) -ForegroundColor Green
            Write-Host "Spans to show: invoke_agent MeetingPrep-… > chat gpt-5.4 (tool_calls) > execute_tool get_engagement_evidence > workplace.get_engagement_evidence (mode, fixture, items)$(if ($Live) { ' > workiq.tool fetch' }) > chat gpt-5.4 (stop)." -ForegroundColor Gray
        }

        'eval' {
            if (-not $Version) { throw 'eval needs -Version (v1.1 or v2). The evaluation never runs on a default.' }
            if (-not $Run) { throw 'eval needs -Run, a label for the report (control, regression, recovery, rehearsal1-control, ...).' }
            $saved = @{}
            foreach ($name in 'FOUNDRY_PROJECT_ENDPOINT', 'WORKPLACE_EVAL', 'WORKPLACE_PROMPT_VERSION', 'WORKPLACE_EVAL_RUN') { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
            try {
                $env:FOUNDRY_PROJECT_ENDPOINT = $Endpoint
                $env:WORKPLACE_EVAL = '1'
                $env:WORKPLACE_PROMPT_VERSION = $Version
                $env:WORKPLACE_EVAL_RUN = $Run
                Write-Step "evaluation: meetingprep-$Version, run '$Run', Replay, gpt-5.4, judge-v2, eight cases"
                Write-Host '  dotnet test tests/Swankers.Evals -c Release --no-build --filter "FullyQualifiedName~MeetingPrepEvals"' -ForegroundColor Gray
                $clock = [Diagnostics.Stopwatch]::StartNew()
                & dotnet test tests/Swankers.Evals -c Release --no-build --nologo --filter 'FullyQualifiedName~MeetingPrepEvals' --logger 'console;verbosity=normal' 2>&1 |
                    Where-Object { $_ -match '^\s*mp-0\d:|Workplace gate|Report:|Passed!|Failed!|Skipped' } | ForEach-Object { Write-Host "  $($_.ToString().Trim())" }
                $clock.Stop()
                $file = Get-LatestReport $Version $Run
                if (-not $file) { Write-Bad "no report written for meetingprep-$Version run '$Run' (skipped? check WORKPLACE_EVAL and the endpoint)"; exit 1 }
                Show-Report $file
                $status = (Get-Content $file.FullName -Raw | ConvertFrom-Json).status
                Write-Host ("evaluation {0} in {1:N0} s" -f $status.ToUpperInvariant(), $clock.Elapsed.TotalSeconds) -ForegroundColor $(if ($status -eq 'pass') { 'Green' } else { 'Red' })
            }
            finally {
                foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
            }
        }

        'show' {
            $file = if ($Report) { Get-Item $Report } elseif ($Version) { Get-LatestReport $Version $Run } else { throw 'show needs -Version (latest report for that version) or -Report <path>.' }
            if (-not $file) { throw "no report for meetingprep-$Version$(if ($Run) { " run '$Run'" }) under $reports" }
            Show-Report $file
        }

        'diff' { Show-Diff }
    }
}
finally {
    Pop-Location
}
