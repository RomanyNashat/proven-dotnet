---
name: docker-dotnet
description: Docker for .NET: multi-stage builds, small runtime images, layer caching, non-root users.
version: 1.0.0
---

# Docker Patterns for .NET

## Four-Stage Dockerfile

<!-- sample: tests/Infra/docker/src/OrderService.Api/Dockerfile -->
```dockerfile
# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS base
WORKDIR /app
EXPOSE 8080
# Chiseled: no shell, no root user, no package manager (~80MB)
# Default user: $APP_UID (non-root)

# Stage 2: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files FIRST for layer caching
COPY ["src/OrderService.Api/OrderService.Api.csproj", "src/OrderService.Api/"]
COPY ["src/OrderService.Domain/OrderService.Domain.csproj", "src/OrderService.Domain/"]
COPY ["src/OrderService.Application/OrderService.Application.csproj", "src/OrderService.Application/"]
COPY ["src/OrderService.Infrastructure/OrderService.Infrastructure.csproj", "src/OrderService.Infrastructure/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]

RUN dotnet restore "src/OrderService.Api/OrderService.Api.csproj"

# Copy everything else
COPY . .

RUN dotnet build "src/OrderService.Api/OrderService.Api.csproj" \
    -c Release --no-restore

# Stage 3: Publish. Tests run in CI before the image is built, not in a stage of it.
FROM build AS publish
RUN dotnet publish "src/OrderService.Api/OrderService.Api.csproj" \
    -c Release --no-build -o /app/publish \
    /p:UseAppHost=false

# Stage 4: Final runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# No HEALTHCHECK: a chiseled image has no shell and no curl, and Docker and Compose run health checks
# inside the container. Kubernetes probes call /health/live from outside (kubernetes-dotnet).
ENTRYPOINT ["dotnet", "OrderService.Api.dll"]
```

Tested in CI (`infra-samples`, on a real Docker): the image builds, runs as a non-root user and answers
`/health/live` from outside. It has no shell, and a `curl` health check on it stays unhealthy for good
while the app answers, which is why there's no `HEALTHCHECK`. There's no test stage either: a
`dotnet test ... || true` stage passes whatever the tests do. Run the tests in CI before the image.

## .dockerignore

<!-- sample: tests/Infra/docker/.dockerignore -->
```
**/.dockerignore
**/.git
**/.gitignore
**/.vs
**/.vscode
**/bin
**/obj
**/node_modules
**/docker-compose*
**/Dockerfile*
**/*.md
**/*.sln.DotSettings
**/coverage
**/test-results
**/.env
**/*.user
**/.idea
```

## Docker Compose (local development)

<!-- sample: tests/Infra/docker/compose.yaml -->
```yaml
services:
  order-service:
    build:
      context: .
      dockerfile: src/OrderService.Api/Dockerfile
      target: final
    ports:
      - "5001:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__Default=Host=postgres;Database=orderdb;Username=app;Password=secret
      - Redis__ConnectionString=redis:6379
      - Kafka__BootstrapServers=kafka:9092
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
      kafka:
        condition: service_healthy
    # No healthcheck here: compose runs it inside the container, and the chiseled image has no shell or
    # curl, so a curl check marks the service unhealthy forever. Check it from the host:
    # curl http://localhost:5001/health/live

  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: orderdb
      POSTGRES_USER: app
      POSTGRES_PASSWORD: secret
    volumes:
      - postgres-data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d orderdb"]
      interval: 5s
      timeout: 3s
      retries: 5

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5

  kafka:
    image: confluentinc/cp-kafka:7.6.0
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller
      KAFKA_LISTENERS: PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT
      KAFKA_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093
      KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
      CLUSTER_ID: MkU3OEVBNTcwNTJENDM2Qk          # a base64 UUID (kafka-storage random-uuid), not a name
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1   # one broker: the default of 3 breaks consumer groups
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
    ports:
      - "9092:9092"
    healthcheck:
      test: ["CMD", "kafka-topics", "--bootstrap-server", "localhost:9092", "--list"]
      interval: 5s
      timeout: 10s
      retries: 20

volumes:
  postgres-data:
```

Tested: `docker compose up --wait` brings every dependency up healthy and the service answers. Kafka in
KRaft mode needs a real cluster id (a base64 UUID from `kafka-storage random-uuid`): tested, the same
broker with `CLUSTER_ID: order-service-local` stops at start. One broker also needs the internal topics'
replication factor set to 1.

Local passwords only: the compose file is for a developer machine, never a shared environment
(`secret-management`).

**On SQL Server**, replace the `postgres` service and the connection string:

```yaml
services:
  order-service:
    # build, ports and the other settings as above
    environment:
      - ConnectionStrings__Default=Server=sqlserver,1433;Database=orderdb;User Id=sa;Password=Local-Dev-2026!;Encrypt=True;TrustServerCertificate=True
    depends_on:
      sqlserver:
        condition: service_healthy

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest   # the major version production runs
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_PID: Developer                               # free for development and testing
      MSSQL_SA_PASSWORD: "Local-Dev-2026!"               # needs upper, lower, digit and symbol
    volumes:
      - sqlserver-data:/var/opt/mssql
    ports:
      - "1433:1433"
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -Q 'SELECT 1' || exit 1"]
      interval: 5s
      timeout: 5s
      retries: 20
      start_period: 20s

volumes:
  sqlserver-data:
```

- The image creates no application database: create `orderdb` and its schema with the reviewed scripts
  (`/migrate script`), the same path as every other environment. Never `Migrate()` on startup.
- `TrustServerCertificate=True` only for the local container's self-signed certificate. `sqlcmd -C`
  trusts it for the health check (the same command the samples CI uses).
- The SQL Server image is x64 only; on an ARM Mac it runs under emulation.

## Image Variants

| Image | Size | Use Case |
|-------|------|----------|
| `aspnet:10.0` | ~220MB | Full image, debugging tools available |
| `aspnet:10.0-alpine` | ~110MB | Smaller, good for staging |
| `aspnet:10.0-noble-chiseled` | ~80MB | Production — no shell, no root, minimal attack surface |
| `aspnet:10.0-noble-chiseled-extra` | ~95MB | Chiseled + globalization support (ICU) |

For services needing globalization (Arabic/Saudi locale), and every service on SQL Server: SqlClient
won't connect without ICU. Tested: the chiseled image sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`,
chiseled-extra doesn't. Alpine and chiseled images also lack tzdata.
The `localization` skill has the startup guard, and which text pattern works without ICU:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS base
# Includes ICU libraries for culture-aware formatting
```

## BuildKit Cache (CI optimization)

```bash
# Build with cache export (GitHub Actions)
docker buildx build \
  --cache-from=type=gha \
  --cache-to=type=gha,mode=max \
  -t order-service:latest \
  -f src/OrderService.Api/Dockerfile .
```

## Multi-Architecture Builds

```bash
# Build for AMD64 and ARM64
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t registry.example.com/order-service:latest \
  --push .
```
