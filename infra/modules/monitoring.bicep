// Log Analytics + workspace-based Application Insights for the azd environment. The Foundry
// project is connected to this Application Insights (modules/shared-access.bicep), so hosted
// agent, MCP server, and web spans land in one resource and show up in Foundry tracing.
param name string
param location string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceName string = workspace.name
output appInsightsName string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
