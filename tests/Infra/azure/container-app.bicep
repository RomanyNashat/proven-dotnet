// Bicep template for Container App. The identity, the environment and the vault already exist; the image
// tag is the commit's (never :latest, which can't be rolled back to).
param location string = resourceGroup().location
param imageTag string

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'order-service-identity'
}

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: 'apps-environment'
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: 'order-service-vault'
}

resource orderApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'order-service'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['https://app.example.com']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE']
        }
      }
      secrets: [
        { name: 'db-connection', keyVaultUrl: '${keyVault.properties.vaultUri}secrets/ConnectionStrings--Default', identity: managedIdentity.id }
      ]
      registries: [
        { server: 'ghcr.io', identity: managedIdentity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'order-service'
          image: 'ghcr.io/myorg/order-service:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Default', secretRef: 'db-connection' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 15 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 10 }
          ]
        }
      ]
      scale: {
        minReplicas: 2
        maxReplicas: 10
        rules: [
          { name: 'http-rule', http: { metadata: { concurrentRequests: '50' } } }
        ]
      }
    }
  }
}
