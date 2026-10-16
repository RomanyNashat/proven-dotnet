---
name: azure-deployment
description: Azure deployment for .NET: App Service/Container Apps, Key Vault, managed identity, and CI/CD to Azure.
version: 1.1.0
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
<!-- sample: tests/Infra/k8s/serviceaccount.yaml -->
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
<!-- sample: tests/Infra/k8s/ingress.yaml -->
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: order-service-ingress
  annotations:
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    # ingress-nginx's names. An annotation it doesn't know (rate-limit, rate-limit-window) is ignored silently.
    nginx.ingress.kubernetes.io/limit-rpm: "100"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
spec:
  ingressClassName: nginx
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

`limit-rpm` is ingress-nginx's rate limit. `rate-limit` and `rate-limit-window` aren't ingress-nginx
annotations: it ignores them, and nothing is limited.

## Azure Container Apps

<!-- sample: tests/Infra/azure/container-app.bicep -->
```bicep
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
```

Built in CI with the Bicep CLI. The identity, the environment and the vault are declared `existing`: used
without a declaration, `bicep build` stops with BCP057 (tested). The image tag is a parameter (the
commit's), never `:latest`.

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

<!-- sample: tests/SkillSamples.Tests/AzureDeployment/AzureSql.cs -->
```csharp
public static class AzureSql
{
    // Azure SQL with a managed identity: SqlClient gets the token itself, so the connection string has no
    // password ("Authentication=Active Directory Default"). UseAzureSql, not UseAzureSqlDefaults (obsolete).
    public static DbContextOptions<OrdersDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<OrdersDbContext>()
            .UseAzureSql(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5))
            .Options;
}
```

<!-- sample: tests/SkillSamples.Tests/AzureDeployment/AzurePostgres.cs -->
```csharp
public static class AzurePostgres
{
    private static readonly string[] Scopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// Azure Database for PostgreSQL with a managed identity. Npgsql has no built-in Entra login, so the token
    /// is the password, fetched again before it expires (tokens last about an hour). Pass the
    /// <c>DefaultAzureCredential</c>; the connection string has no password.
    /// </summary>
    public static NpgsqlDataSource DataSource(string connectionString, TokenCredential credential, TimeSpan refreshEvery)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct)).Token,
            successRefreshInterval: refreshEvery,           // 55 minutes in production
            failureRefreshInterval: TimeSpan.FromSeconds(1));
        return builder.Build();
    }
}
```

Tested against real servers (`AzureDeployment/AzureTests.cs`): the token is used as the PostgreSQL
password, and when the first token is refused (a role granted a minute late), the next one fetched on the
timer works, with the same data source and no restart. `UseAzureSql` connects with retries on, and
`UseAzureSqlDefaults` is marked obsolete.

`UsePeriodicPasswordProvider` is tested in `secret-management` (password rotation); the Entra token
scope and the `Authentication=` keyword are from the providers' documentation, not run in CI.

## Configuration Hierarchy (Azure)
```
appsettings.json                    → defaults (committed)
appsettings.Production.json         → production overrides (committed, no secrets)
Azure Key Vault                     → secrets (connection strings, API keys)
Environment variables               → runtime overrides (K8s ConfigMaps)
```
