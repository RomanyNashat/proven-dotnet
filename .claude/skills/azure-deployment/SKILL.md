---
name: azure-deployment
description: Azure deployment for .NET: App Service/Container Apps, Key Vault, managed identity, and CI/CD to Azure.
version: 1.0.0
---

# Azure Deployment Patterns

## Decision Matrix

| Scenario | Use | Why |
|----------|-----|-----|
| Full microservices with K8s control | **AKS** | Network policies, custom ingress, pod autoscaling |
| Simpler microservices, less ops | **Container Apps** | Managed K8s, auto-scaling, Dapr integration |
| Monolith or simple API | **App Service** | PaaS, slot deployments, minimal config |
| Background workers | **Container Apps Jobs** | Event-driven, scale-to-zero |
| Scheduled tasks | **Container Apps Jobs** | Cron triggers, auto-retry |

## AKS Configuration

### Workload Identity (passwordless auth to Azure services)
```yaml
# Service Account with Azure Workload Identity
apiVersion: v1
kind: ServiceAccount
metadata:
  name: order-service-sa
  namespace: order-service
  annotations:
    azure.workload.identity/client-id: "<managed-identity-client-id>"
  labels:
    azure.workload.identity/use: "true"
```

```csharp
// .NET: DefaultAzureCredential auto-detects Workload Identity on AKS
var credential = new DefaultAzureCredential();

// Works for: Key Vault, Blob Storage, SQL Database, Service Bus, etc.
builder.Configuration.AddAzureKeyVault(
    new Uri("https://order-service-vault.vault.azure.net/"),
    credential);
```

### Ingress with NGINX
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: order-service-ingress
  annotations:
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    nginx.ingress.kubernetes.io/rate-limit-window: "1m"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
spec:
  tls:
    - hosts: [api.example.com]
      secretName: api-tls
  rules:
    - host: api.example.com
      http:
        paths:
          - path: /api/orders
            pathType: Prefix
            backend:
              service:
                name: order-service
                port:
                  number: 80
```

## Azure Container Apps

```bicep
// Bicep template for Container App
resource orderApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'order-service'
  location: resourceGroup().location
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
          image: 'ghcr.io/myorg/order-service:latest'
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
```

## App Service (slot deployment)

```bash
# Deploy to staging slot
az webapp deployment source config-zip \
  --resource-group mygroup \
  --name order-service \
  --slot staging \
  --src publish.zip

# Verify staging
curl https://order-service-staging.azurewebsites.net/health/ready

# Swap staging → production (zero downtime)
az webapp deployment slot swap \
  --resource-group mygroup \
  --name order-service \
  --slot staging \
  --target-slot production

# Rollback if needed
az webapp deployment slot swap \
  --resource-group mygroup \
  --name order-service \
  --slot production \
  --target-slot staging
```

## Azure Database Configuration

```csharp
// Azure SQL with Managed Identity: SqlClient gets the token itself. No password in the string:
// Server=tcp:myserver.database.windows.net,1433;Database=orderdb;Authentication=Active Directory Default;Encrypt=True;
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseAzureSql(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));   // not UseAzureSqlDefaults: obsolete in EF 10

// Azure Database for PostgreSQL Flexible Server with Managed Identity: Npgsql has no built-in Entra
// login, so the token is the password, fetched on a timer (tokens last about an hour).
// Host=myserver.postgres.database.azure.com;Database=orderdb;Username=managed-identity-name;Ssl Mode=Require;
var credential = new DefaultAzureCredential();
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UsePeriodicPasswordProvider(
    async (_, ct) => (await credential.GetTokenAsync(
        new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]), ct)).Token,
    successRefreshInterval: TimeSpan.FromMinutes(55),
    failureRefreshInterval: TimeSpan.FromSeconds(10));
builder.Services.AddSingleton(dataSourceBuilder.Build());
builder.Services.AddDbContextPool<AppDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
```

`UsePeriodicPasswordProvider` is tested in `secret-management` (password rotation); the Entra token
scope and the `Authentication=` keyword are from the providers' documentation, not run in CI.

## Configuration Hierarchy (Azure)
```
appsettings.json                    → defaults (committed)
appsettings.Production.json         → production overrides (committed, no secrets)
Azure Key Vault                     → secrets (connection strings, API keys)
Environment variables               → runtime overrides (K8s ConfigMaps)
```
