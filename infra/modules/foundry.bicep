// Shared Microsoft Foundry account, project, and chat model deployment.
// Lives in rg-swankers-shared (like the Key Vault) so azd down/up in Phase 4 never recreates
// the model deployment or its quota. Phase 4's azd environment references these as existing.
// Shapes follow microsoft-foundry/foundry-samples infrastructure-setup-bicep/00-basic.
targetScope = 'resourceGroup'

@description('Foundry account name. Also the custom subdomain, so it must be globally unique.')
param accountName string

@description('Project under the account.')
param projectName string = 'swankers-coach'

param location string = resourceGroup().location

@description('One chat deployment serves both the agent and the eval judge.')
param modelName string = 'gpt-5.4'
param modelVersion string = '2026-03-05'
param modelFormat string = 'OpenAI'
param deploymentSku string = 'GlobalStandard'

@description('Capacity in thousands of tokens per minute.')
param modelCapacity int = 50

@description('Concurrency policy requires Client, PrimaryOwner, and ExpectedDeleteDate on every resource.')
param tags object

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    // Entra only: local dev uses DefaultAzureCredential, deployed services use managed identity.
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Swankers Coach'
    description: 'Fantasy football coaching agent demo (Cloud & AI Summit 2026).'
  }
}

resource chatModel 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: modelName
  tags: tags
  // The provider rejects concurrent child operations on a new account (RequestConflict);
  // create the deployment after the project.
  dependsOn: [project]
  sku: {
    name: deploymentSku
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: modelFormat
      name: modelName
      version: modelVersion
    }
  }
}

output accountName string = account.name
output accountId string = account.id
output projectName string = project.name
output projectEndpoint string = 'https://${account.name}.services.ai.azure.com/api/projects/${project.name}'
output modelDeploymentName string = chatModel.name
output projectPrincipalId string = project.identity.principalId
