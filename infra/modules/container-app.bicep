// One Swankers service on Container Apps: a user-assigned identity (AcrPull here; Key Vault and
// Foundry access are granted by the caller), external HTTPS ingress, and the image azd deploy
// publishes. Until the first deploy the app runs a public placeholder image, so provisioning
// never waits on an image that does not exist yet. One replica only: SimLeague state lives on
// the container's disk.
param name string
param location string
param tags object

@description('azd-service-name tag: how azd deploy finds this app.')
param serviceName string
param environmentId string
param registryName string

@description('Image published by azd deploy (SERVICE_<name>_IMAGE_NAME); empty before the first deploy.')
param image string = ''
param targetPort int = 8080

@description('Environment variables as { name, value } objects.')
param env array = []
param cpu string = '0.5'
param memory string = '1Gi'

var acrPullRole = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var placeholderImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var hasImage = !empty(image)

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}'
  location: location
  tags: tags
}

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, acrPullRole)
  properties: {
    principalId: identity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalType: 'ServicePrincipal'
  }
}

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': serviceName })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  dependsOn: [pull]
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: true
        targetPort: hasImage ? targetPort : 80
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: serviceName
          image: hasImage ? image : placeholderImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          // DefaultAzureCredential picks the user-assigned identity by AZURE_CLIENT_ID.
          env: concat(env, [
            {
              name: 'AZURE_CLIENT_ID'
              value: identity.properties.clientId
            }
          ])
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output principalId string = identity.properties.principalId
