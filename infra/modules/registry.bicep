// Container Registry for the two Container Apps images. azd builds the images here with the
// registry's remote build (no local Docker); the apps pull with their managed identity.
param name string
param location string
param tags object

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // Pulls use AcrPull on the app identities; the remote build uses the deployer's ARM token.
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
