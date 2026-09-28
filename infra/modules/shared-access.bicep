// Runs in the shared resource group (Key Vault + Foundry, see modules/foundry.bicep): grants
// the azd environment's identities access to the shared resources and connects the Foundry
// project to this environment's Application Insights so hosted-agent traces land there.
// Nothing here is created in the shared group except role assignments and one connection.
param keyVaultName string
param foundryAccountName string
param foundryProjectName string

@description('Resource group of the azd environment that owns the Application Insights component.')
param appInsightsResourceGroupName string
param appInsightsName string

@description('Identity of the Swankers.Mcp container app: reads Mcp--* and Mfl--* secrets.')
param mcpPrincipalId string

@description('Identity of the Swankers.Web container app: reads the demo admin key and calls the Coach agent endpoint.')
param webPrincipalId string

@description('Identity of the Coach hosted agent (created by Foundry on first deploy); reads the MCP credential secret. Empty until the agent exists.')
param coachAgentPrincipalId string = ''

// Built-in role ids (stable across tenants).
var keyVaultSecretsUserRole = '4633458b-17de-408a-b874-0445c86b69e6'
var foundryUserRole = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: account
  name: foundryProjectName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
  scope: resourceGroup(appInsightsResourceGroupName)
}

resource mcpSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, mcpPrincipalId, keyVaultSecretsUserRole)
  properties: {
    principalId: mcpPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole)
    principalType: 'ServicePrincipal'
  }
}

resource webSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, webPrincipalId, keyVaultSecretsUserRole)
  properties: {
    principalId: webPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole)
    principalType: 'ServicePrincipal'
  }
}

// The web app talks to the Coach agent endpoint (Phase 6) through the project.
resource webFoundry 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, webPrincipalId, foundryUserRole)
  properties: {
    principalId: webPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', foundryUserRole)
    principalType: 'ServicePrincipal'
  }
}

resource coachSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(coachAgentPrincipalId)) {
  scope: vault
  name: guid(vault.id, coachAgentPrincipalId, keyVaultSecretsUserRole)
  properties: {
    principalId: coachAgentPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole)
    principalType: 'ServicePrincipal'
  }
}

// Foundry tracing: the project's Application Insights connection. The hosted runtime injects
// this connection string into the Coach container; the portal's Tracing view reads from it.
// A project can hold only one AppInsights connection. Shape follows foundry-samples
// infrastructure-setup-bicep/01-connections/connection-application-insights.bicep.
resource tracing 'Microsoft.CognitiveServices/accounts/projects/connections@2025-06-01' = {
  parent: project
  name: appInsightsName
  properties: {
    category: 'AppInsights'
    target: appInsights.id
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: {
      key: appInsights.properties.ConnectionString
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: appInsights.id
    }
  }
}

output keyVaultUri string = vault.properties.vaultUri
output projectEndpoint string = 'https://${account.name}.services.ai.azure.com/api/projects/${project.name}'
output projectPrincipalId string = project.identity.principalId
