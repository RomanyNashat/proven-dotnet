---
name: kubernetes-dotnet
description: Kubernetes for .NET: health probes, resource limits/QoS, graceful shutdown, config/secrets, rolling deploys.
version: 1.0.0
---

# Kubernetes Patterns for .NET

## Deployment Manifest

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
  labels:
    app: order-service
    version: "1.0.0"
spec:
  replicas: 3
  revisionHistoryLimit: 5
  strategy:
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0  # zero-downtime
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
        version: "1.0.0"
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    spec:
      serviceAccountName: order-service-sa
      terminationGracePeriodSeconds: 60
      containers:
        - name: order-service
          image: registry.example.com/order-service:1.0.0
          ports:
            - containerPort: 8080
              name: http
              protocol: TCP
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: "Production"
            - name: DOTNET_EnableDiagnostics
              value: "0"
          envFrom:
            - configMapRef:
                name: order-service-config
          resources:
            requests:
              cpu: "250m"
              memory: "256Mi"
            limits:
              cpu: "1000m"
              memory: "512Mi"
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 15
            timeoutSeconds: 3
            failureThreshold: 3
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3
          startupProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 5
            failureThreshold: 30  # 5 * 30 = 150s max startup
          volumeMounts:
            - name: secrets
              mountPath: /mnt/secrets
              readOnly: true
      volumes:
        - name: secrets
          csi:
            driver: secrets-store.csi.k8s.io
            readOnly: true
            volumeAttributes:
              secretProviderClass: order-service-secrets
```

## Health Checks (.NET side)

<!-- sample: tests/SkillSamples.Tests/Kubernetes/Probes.cs -->
```csharp
public static class Probes
{
    public static IHealthChecksBuilder AddProbes(this IServiceCollection services, string redisConnectionString) =>
        services.AddHealthChecks()
            // Liveness: is the process alive? Nothing outside it, or an outage restarts every pod.
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            // Readiness: can it serve traffic? One check per dependency (.AddNpgSql, .AddSqlServer, .AddKafka).
            .AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);

    public static WebApplication MapProbes(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        return app;
    }
}
```

Tested with Redis down: `/health/ready` answers 503 and `/health/live` 200. Kubernetes takes the pod out
of the Service and leaves it running. Put Redis in liveness and an outage restarts every pod, over and
over, without bringing Redis back.

## HorizontalPodAutoscaler

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  minReplicas: 3
  maxReplicas: 20
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
    - type: Resource
      resource:
        name: memory
        target:
          type: Utilization
          averageUtilization: 80
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300  # wait 5 min before scaling down
      policies:
        - type: Percent
          value: 25
          periodSeconds: 60
    scaleUp:
      stabilizationWindowSeconds: 30
      policies:
        - type: Percent
          value: 50
          periodSeconds: 60
```

## PodDisruptionBudget

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: order-service-pdb
spec:
  minAvailable: 2  # always keep at least 2 pods running
  selector:
    matchLabels:
      app: order-service
```

## NetworkPolicy (zero-trust)

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: order-service-netpol
spec:
  podSelector:
    matchLabels:
      app: order-service
  policyTypes:
    - Ingress
    - Egress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: api-gateway
      ports:
        - protocol: TCP
          port: 8080
    - from:
        - podSelector:
            matchLabels:
              app: inventory-service  # gRPC calls
      ports:
        - protocol: TCP
          port: 8080
  egress:
    - to:
        - podSelector:
            matchLabels:
              app: postgresql          # SQL Server: its label
      ports:
        - protocol: TCP
          port: 5432                   # SQL Server: 1433
    - to:
        - podSelector:
            matchLabels:
              app: redis
      ports:
        - protocol: TCP
          port: 6379
    - to:  # Kafka
        - podSelector:
            matchLabels:
              app: kafka
      ports:
        - protocol: TCP
          port: 9092
    - to:  # DNS
        - namespaceSelector: {}
      ports:
        - protocol: UDP
          port: 53
```

## Azure Key Vault CSI Driver

```yaml
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: order-service-secrets
spec:
  provider: azure
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "false"
    clientID: "<workload-identity-client-id>"
    keyvaultName: "order-service-vault"
    tenantId: "<azure-tenant-id>"
    objects: |
      array:
        - |
          objectName: ConnectionStrings--Default
          objectType: secret
        - |
          objectName: Kafka--SaslPassword
          objectType: secret
        - |
          objectName: Redis--Password
          objectType: secret
  secretObjects:
    - secretName: order-service-secrets
      type: Opaque
      data:
        - objectName: ConnectionStrings--Default
          key: ConnectionStrings__Default
```

## Memory limits and restarts

.NET caps the GC heap at 75% of the container memory limit by default. `OOMKilled` (exit code 137)
means the whole process passed the limit and was killed with no log and no dump. Diagnosing that, and
other production problems, is in `production-diagnostics`.

## Graceful Shutdown

On SIGTERM the host cancels every `BackgroundService`'s token and waits for them, but only for
`HostOptions.ShutdownTimeout` (30 s by default, tested). Work still running after that is cut off
mid-batch. Tested: a worker that needs 3 s to finish its batch finishes with a 10 s timeout and is cut
with a 1 s one.

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
```

A pod that's stopping can still get requests for a few seconds while the ingress updates its endpoints,
and those requests fail with 502. A short `preStop` sleep (5–10 s) before SIGTERM covers that window.
Keep `terminationGracePeriodSeconds` above the sleep plus `ShutdownTimeout`, or Kubernetes kills the
process before the host has finished. More 502 causes in `nginx`.
