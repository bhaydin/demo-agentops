// Swankers Coach azd environment: a disposable resource group (rg-<env>) holding Log Analytics,
// Application Insights, a Container Registry, a Container Apps environment, and the Swankers.Mcp
// and Swankers.Web container apps. Secrets and the Foundry account stay in rg-swankers-shared
// (modules/foundry.bicep, deployed once by the maintainer) and are referenced, never recreated,
// so azd down/up during rehearsals never touches the model deployment or the vault.
// The Coach hosted agent is not provisioned here: tools/Swankers.AgentDeploy publishes it to
// the shared project after azd up (see README.md, "Deploy to Azure").
targetScope = 'subscription'

@minLength(1)
@maxLength(40)
@description('azd environment name; the resource group and resource names derive from it.')
param environmentName string

@minLength(1)
@description('Region for the environment. North Central US: hosted agents plus cloud red teaming (docs/ARCHITECTURE.md).')
param location string

@description('Resource group holding the shared Key Vault and Foundry account.')
param sharedResourceGroupName string = 'rg-swankers-shared'
param keyVaultName string = 'kv-swankers-vxzd'
param foundryAccountName string = 'foundry-swankers-vxzd'
param foundryProjectName string = 'swankers-coach'
param modelDeploymentName string = 'gpt-5.4'

// Concurrency management-group policy denies any resource missing these three tags.
@minLength(1)
param tagClient string
@minLength(1)
param tagPrimaryOwner string
@minLength(1)
param tagExpectedDeleteDate string

@description('DEMO: false disables the confirmation gate for commissioner-credential calls (the Friday "before"). The hardened default is true. See docs/ARCHITECTURE.md#security-demo.')
param commissionerGateEnabled bool = true

@description('Images published by azd deploy (SERVICE_<name>_IMAGE_NAME); empty before the first deploy.')
param mcpImageName string = ''
param webImageName string = ''

@description('Principal id of the Coach hosted agent identity (COACH_AGENT_PRINCIPAL_ID). Set after the first agent deploy; grants it the MCP credential secret.')
param coachAgentPrincipalId string = ''

var tags = {
  'azd-env-name': environmentName
  Client: tagClient
  PrimaryOwner: tagPrimaryOwner
  ExpectedDeleteDate: tagExpectedDeleteDate
}

var keyVaultUri = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/'
var projectEndpoint = 'https://${foundryAccountName}.services.ai.azure.com/api/projects/${foundryProjectName}'
var registryName = 'cr${replace(environmentName, '-', '')}${uniqueString(subscription().id, environmentName)}'

resource rg 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    name: environmentName
    location: location
    tags: tags
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  scope: rg
  params: {
    name: registryName
    location: location
    tags: tags
  }
}

module containerApps 'modules/container-apps-env.bicep' = {
  name: 'container-apps-env'
  scope: rg
  params: {
    name: environmentName
    location: location
    tags: tags
    logAnalyticsWorkspaceName: monitoring.outputs.workspaceName
  }
}

module mcp 'modules/container-app.bicep' = {
  name: 'mcp'
  scope: rg
  params: {
    name: 'ca-${environmentName}-mcp'
    serviceName: 'mcp'
    location: location
    tags: tags
    environmentId: containerApps.outputs.id
    registryName: registry.outputs.name
    image: mcpImageName
    env: [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'KeyVault__Uri', value: keyVaultUri }
      { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.connectionString }
      // DEMO: intentionally vulnerable when false (Friday talk). See docs/ARCHITECTURE.md#security-demo.
      { name: 'Mcp__CommissionerGateEnabled', value: string(commissionerGateEnabled) }
    ]
  }
}

module web 'modules/container-app.bicep' = {
  name: 'web'
  scope: rg
  params: {
    name: 'ca-${environmentName}-web'
    serviceName: 'web'
    location: location
    tags: tags
    environmentId: containerApps.outputs.id
    registryName: registry.outputs.name
    image: webImageName
    env: [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      // Ingress terminates TLS; honor X-Forwarded-* so HTTPS redirection does not loop.
      { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
      { name: 'KeyVault__Uri', value: keyVaultUri }
      { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.connectionString }
      { name: 'Mcp__BaseUrl', value: 'https://${mcp.outputs.fqdn}' }
      { name: 'Coach__ProjectEndpoint', value: projectEndpoint }
      { name: 'Coach__AgentName', value: 'Coach' }
    ]
  }
}

module sharedAccess 'modules/shared-access.bicep' = {
  name: 'shared-access-${environmentName}'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    foundryAccountName: foundryAccountName
    foundryProjectName: foundryProjectName
    appInsightsResourceGroupName: rg.name
    appInsightsName: monitoring.outputs.appInsightsName
    mcpPrincipalId: mcp.outputs.principalId
    webPrincipalId: web.outputs.principalId
    coachAgentPrincipalId: coachAgentPrincipalId
  }
}

// azd writes these to the environment; tools/Swankers.AgentDeploy and the README use them.
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.outputs.loginServer
output APPLICATIONINSIGHTS_NAME string = monitoring.outputs.appInsightsName
output KEYVAULT_URI string = keyVaultUri
output FOUNDRY_PROJECT_ENDPOINT string = projectEndpoint
output AZURE_AI_MODEL_DEPLOYMENT_NAME string = modelDeploymentName
output MCP_BASE_URL string = 'https://${mcp.outputs.fqdn}'
output MCP_ENDPOINT string = 'https://${mcp.outputs.fqdn}/mcp'
output WEB_URL string = 'https://${web.outputs.fqdn}'
