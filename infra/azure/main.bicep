targetScope = 'subscription'

@description('Short lowercase prefix used to name Azure resources.')
@minLength(3)
@maxLength(18)
param baseName string = 'nimbusflow'

@description('Azure region for the resource group and platform resources.')
param location string = 'centralus'

@description('Azure SQL administrator login used by the deployment workflow.')
param sqlAdminLogin string

@secure()
@description('Azure SQL administrator password. Store this only as a GitHub environment secret.')
param sqlAdminPassword string

var resourceGroupName = 'rg-${baseName}-${uniqueString(subscription().subscriptionId)}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    application: 'NimbusFlow'
    managedBy: 'Bicep'
    workload: 'workflow-automation'
  }
}

module core 'core.bicep' = {
  name: 'nimbusflow-core'
  scope: resourceGroup
  params: {
    baseName: baseName
    location: location
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
  }
}

output resourceGroupName string = resourceGroup.name
output acrName string = core.outputs.acrName
output acrLoginServer string = core.outputs.acrLoginServer
output containerAppsEnvironmentName string = core.outputs.containerAppsEnvironmentName
output serviceBusNamespace string = core.outputs.serviceBusNamespace
output serviceBusQueueName string = core.outputs.serviceBusQueueName
output sqlServerName string = core.outputs.sqlServerName
output apiIdentityName string = core.outputs.apiIdentityName
output workerIdentityName string = core.outputs.workerIdentityName
