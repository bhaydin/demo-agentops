# Shared by demo/reset.ps1 and demo/stage.ps1 (dot-source it): where the MCP demo REST API is and
# the key it needs. Values come from the selected azd environment and Key Vault (after `az login`),
# or from -Local / -BaseUrl / -AdminKey / MCP_DEMO_ADMIN_KEY. Nothing secret is printed.

function Get-DemoApi {
    param(
        [string] $Repo,
        [switch] $Local,
        [string] $BaseUrl,
        [string] $AdminKey,
        [string] $VaultName = 'kv-swankers-vxzd',
        [string] $KeySecretName = 'Mcp--DemoAdminKey'
    )

    if (-not $BaseUrl) {
        if ($Local) {
            $BaseUrl = 'http://localhost:5210'
        }
        else {
            $line = (& azd env get-values --cwd $Repo 2>$null) | Where-Object { $_ -like 'MCP_BASE_URL=*' } | Select-Object -Last 1
            if (-not $line) { throw 'No MCP_BASE_URL in the selected azd environment; pass -BaseUrl or -Local.' }
            $BaseUrl = $line.Substring('MCP_BASE_URL='.Length).Trim('"')
        }
    }

    if (-not $AdminKey) { $AdminKey = $env:MCP_DEMO_ADMIN_KEY }
    if (-not $AdminKey -and -not $Local) {
        $AdminKey = & az keyvault secret show --vault-name $VaultName --name $KeySecretName --query value -o tsv
        if ($LASTEXITCODE -ne 0 -or -not $AdminKey) { throw "Could not read $KeySecretName from $VaultName; pass -AdminKey." }
    }
    if (-not $AdminKey) { throw 'No admin key: set MCP_DEMO_ADMIN_KEY or pass -AdminKey.' }

    return @{ BaseUrl = $BaseUrl.TrimEnd('/'); Headers = @{ 'X-Demo-Admin-Key' = $AdminKey } }
}

function Get-LeagueState {
    param([hashtable] $Api)
    return Invoke-RestMethod -Method Get -Uri "$($Api.BaseUrl)/api/state" -Headers $Api.Headers -TimeoutSec 20
}
