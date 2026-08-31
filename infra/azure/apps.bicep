@description('The same prefix used by the core infrastructure deployment.')
@minLength(3)
@maxLength(18)
param baseName string = 'nimbusflow'

param apiImage string

param workerImage string

param sqlAdminLogin string

@secure()
param sqlAdminPassword string

var suffix = uniqueString(subscription().subscriptionId, resourceGroup().id)
var queueName = 'workflow-jobs'
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=NimbusFlow;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var commonTags = {
  application: 'NimbusFlow'
  managedBy: 'Bicep'
  environment: 'production'
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-${baseName}-${suffix}'
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: 'acr${suffix}'
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: 'sb-${baseName}-${suffix}'
}

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' existing = {
  name: 'sql-${baseName}-${suffix}'
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-${baseName}-api-${suffix}'
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-${baseName}-worker-${suffix}'
}

resource api 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-${baseName}-api'
  location: resourceGroup().location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiIdentity.id
        }
      ]
      secrets: [
        {
          name: 'sql-connection'
          value: sqlConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__NimbusFlow', secretRef: 'sql-connection' }
            { name: 'ServiceBus__ConnectionString', value: '' }
            { name: 'ServiceBus__FullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
            { name: 'ServiceBus__ManagedIdentityClientId', value: apiIdentity.properties.clientId }
            { name: 'ServiceBus__QueueName', value: queueName }
            { name: 'Database__MigrateOnStartup', value: 'true' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 15
              periodSeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 15
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-concurrency'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-${baseName}-worker'
  location: resourceGroup().location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: workerIdentity.id
        }
      ]
      secrets: [
        {
          name: 'sql-connection'
          value: sqlConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__NimbusFlow', secretRef: 'sql-connection' }
            { name: 'ServiceBus__ConnectionString', value: '' }
            { name: 'ServiceBus__FullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
            { name: 'ServiceBus__ManagedIdentityClientId', value: workerIdentity.properties.clientId }
            { name: 'ServiceBus__QueueName', value: queueName }
            { name: 'WorkflowExecution__StepDelayMilliseconds', value: '500' }
            { name: 'WorkflowExecution__RetryBaseDelaySeconds', value: '30' }
            { name: 'WorkflowExecution__ExecutionLeaseSeconds', value: '900' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
        pollingInterval: 10
        cooldownPeriod: 60
        rules: [
          {
            name: 'servicebus-backlog'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                namespace: serviceBus.name
                queueName: queueName
                messageCount: '5'
              }
              identity: workerIdentity.id
            }
          }
        ]
      }
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output apiName string = api.name
output workerName string = worker.name
