#!/usr/bin/env pwsh
<#
.SYNOPSIS
Apply the public repository's main-branch and Azure environment authorization gates.
.DESCRIPTION
Requires repository administration through gh. Does not deploy anything. The owner may
bypass review only through a PR; build checks, PR use, force-push and deletion rules have
no bypass. Environment self-review is allowed so the sole maintainer can run the demo,
but skipping environment protection is disabled. See docs/REPOSITORY-SECURITY.md.
#>
[CmdletBinding()]
param(
    [string] $Repo = 'bhaydin/demo-agentops',
    [string] $Environment = 'foundry',
    [string] $EvalsEnvironment = 'evals'
)

$ErrorActionPreference = 'Stop'

function Invoke-GitHub {
    param([string] $Path, [string] $Method = 'GET', $Body)
    $argsList = @('api', $Path, '--method', $Method)
    $file = $null
    try {
        if ($null -ne $Body) {
            $file = New-TemporaryFile
            $Body | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $file -Encoding utf8
            $argsList += @('--input', "$file")
        }
        $response = & gh @argsList
        if ($LASTEXITCODE -ne 0) { throw "GitHub $Method $Path failed." }
        if ($response) { return ($response | ConvertFrom-Json) }
    }
    finally { if ($file) { Remove-Item -LiteralPath $file -Force } }
}

$repoInfo = Invoke-GitHub "repos/$Repo"
if (-not $repoInfo.permissions.admin) { throw 'Repository administration permission is required.' }
if ($repoInfo.owner.type -ne 'User') { throw 'This policy expects a personal repository with one owner.' }
$ownerId = $repoInfo.owner.id
$conditions = @{ ref_name = @{ include = @('refs/heads/main'); exclude = @() } }
$existing = @(Invoke-GitHub "repos/$Repo/rulesets")

$prParameters = @{
    required_approving_review_count = 0
    dismiss_stale_reviews_on_push = $true
    require_code_owner_review = $false
    require_last_push_approval = $false
    required_review_thread_resolution = $true
}
$integrity = @{
    name = 'main - required checks and history'
    target = 'branch'; enforcement = 'active'; bypass_actors = @(); conditions = $conditions
    rules = @(
        @{ type = 'deletion' }
        @{ type = 'non_fast_forward' }
        @{ type = 'pull_request'; parameters = $prParameters }
        @{ type = 'required_status_checks'; parameters = @{
            strict_required_status_checks_policy = $true
            do_not_enforce_on_create = $false
            required_status_checks = @(@{ context = 'build'; integration_id = 15368 }) # GitHub Actions
        } }
    )
}
$reviewParameters = $prParameters.Clone()
$reviewParameters.required_approving_review_count = 1
$reviewParameters.require_code_owner_review = $true
$reviewParameters.require_last_push_approval = $true
$review = @{
    name = 'main - maintainer review'
    target = 'branch'; enforcement = 'active'; conditions = $conditions
    # RepositoryRole 5 is the repository administrator (currently only @bhaydin).
    # This bypass permits the sole maintainer to merge their own PR, never a direct push.
    bypass_actors = @(@{ actor_type = 'RepositoryRole'; actor_id = 5; bypass_mode = 'pull_request' })
    rules = @(@{ type = 'pull_request'; parameters = $reviewParameters })
}
foreach ($policy in @($integrity, $review)) {
    $match = @($existing | Where-Object name -eq $policy.name)
    if ($match.Count -gt 1) { throw "Duplicate rulesets named $($policy.name)." }
    if ($match.Count -eq 1) {
        Invoke-GitHub "repos/$Repo/rulesets/$($match[0].id)" PUT $policy | Out-Null
    }
    else { Invoke-GitHub "repos/$Repo/rulesets" POST $policy | Out-Null }
    Write-Host "Enforced $($policy.name)"
}

foreach ($name in @($Environment, $EvalsEnvironment)) {
    $encoded = [Uri]::EscapeDataString($name)
    Invoke-GitHub "repos/$Repo/environments/$encoded" PUT @{
        reviewers = @(@{ type = 'User'; id = $ownerId })
        prevent_self_review = $false
        can_admins_bypass = $false
        deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
    } | Out-Null
    $path = "repos/$Repo/environments/$encoded/deployment-branch-policies"
    $policies = (Invoke-GitHub $path).branch_policies
    # Remove every broader branch/tag allowance. A tag called main must not qualify.
    foreach ($policy in $policies) {
        if ($policy.name -ne 'main' -or $policy.type -ne 'branch') {
            Invoke-GitHub "$path/$($policy.id)" DELETE | Out-Null
        }
    }
    if (-not @($policies | Where-Object { $_.name -eq 'main' -and $_.type -eq 'branch' }).Count) {
        Invoke-GitHub $path POST @{ name = 'main'; type = 'branch' } | Out-Null
    }
    Write-Host "Protected environment ${name}: owner approval, main branch only, no admin bypass"
}

Invoke-GitHub "repos/$Repo/actions/permissions/fork-pr-contributor-approval" PUT @{
    approval_policy = 'all_external_contributors'
} | Out-Null
Invoke-GitHub "repos/$Repo/actions/permissions/workflow" PUT @{
    default_workflow_permissions = 'read'; can_approve_pull_request_reviews = $false
} | Out-Null
# Preserve the existing action allow-list while requiring immutable action references.
$actions = Invoke-GitHub "repos/$Repo/actions/permissions"
Invoke-GitHub "repos/$Repo/actions/permissions" PUT @{
    enabled = $actions.enabled; allowed_actions = $actions.allowed_actions; sha_pinning_required = $true
} | Out-Null
Write-Host 'Fork workflow approval, read-only default tokens, and action SHA pinning enforced.'
