---
name: cicd-github-actions
description: GitHub Actions for .NET: build/test/publish pipelines, caching, matrix builds, secrets, container publishing. The workflows shown pass actionlint (with shellcheck) in CI.
version: 1.1.0
---

# GitHub Actions CI/CD Patterns

Every workflow below is a file in `tests/Workflows/` that CI checks with actionlint, including shellcheck
on the `run:` scripts. That proves they're valid workflows with sound shell; it doesn't run them against
your registry or cluster.

## PR Validation Pipeline

<!-- sample: tests/Workflows/pr-validation.yml -->
```yaml
name: PR Validation

on:
  pull_request:
    branches: [main, develop]
    paths-ignore:
      - '**/*.md'
      - '.github/ISSUE_TEMPLATE/**'

concurrency:
  group: pr-${{ github.event.pull_request.number }}
  cancel-in-progress: true

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: testdb
          POSTGRES_USER: test
          POSTGRES_PASSWORD: test
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 5s
          --health-timeout 3s
          --health-retries 5
      redis:
        image: redis:7-alpine
        ports:
          - 6379:6379

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj', '**/Directory.Packages.props') }}
          restore-keys: nuget-${{ runner.os }}-

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore -warnaserror

      - name: Unit Tests
        run: |
          dotnet test --no-build \
            --filter "Category!=Integration" \
            --logger "trx;LogFileName=unit-tests.trx" \
            /p:CollectCoverage=true \
            /p:CoverletOutputFormat=cobertura \
            /p:CoverletOutput=./coverage/ \
            /p:Threshold=80 \
            /p:ThresholdType=line

      - name: Architecture Tests
        run: dotnet test --no-build --filter "Category=Architecture"

      - name: Integration Tests
        env:
          ConnectionStrings__PostgreSQL: "Host=localhost;Database=testdb;Username=test;Password=test"
          Redis__ConnectionString: "localhost:6379"
        run: |
          dotnet test --no-build \
            --filter "Category=Integration" \
            --logger "trx;LogFileName=integration-tests.trx"

      - name: NuGet Vulnerability Check
        run: |
          if dotnet list package --vulnerable --include-transitive 2>&1 | tee /dev/stderr | grep -q "has the following vulnerable"; then
            echo "::error::A package has a known vulnerability."
            exit 1
          fi

      - name: Upload Coverage
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: coverage-report
          path: '**/coverage/*.cobertura.xml'

      - name: Upload Test Results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/*.trx'
```

## Deploy Pipeline

<!-- sample: tests/Workflows/deploy.yml -->
```yaml
name: Deploy

on:
  push:
    branches: [main]
  workflow_dispatch:
    inputs:
      environment:
        description: 'Target environment'
        required: true
        type: choice
        options: [staging, production]

permissions:
  id-token: write   # OIDC token for Azure
  contents: read
  packages: write    # push Docker images

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}/order-service

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.meta.outputs.version }}

    steps:
      - uses: actions/checkout@v4

      - name: Docker meta
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,prefix=
            type=ref,event=branch
            type=semver,pattern={{version}}

      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          file: src/OrderService.Api/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
          platforms: linux/amd64

  deploy-staging:
    needs: build-and-push
    runs-on: ubuntu-latest
    environment: staging

    steps:
      - uses: actions/checkout@v4

      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Set AKS context
        uses: azure/aks-set-context@v4
        with:
          resource-group: ${{ secrets.AKS_RESOURCE_GROUP }}
          cluster-name: ${{ secrets.AKS_CLUSTER_NAME }}

      - name: Deploy to AKS
        run: |
          kubectl set image deployment/order-service \
            order-service=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }} \
            -n order-service
          kubectl rollout status deployment/order-service \
            -n order-service --timeout=300s

  deploy-production:
    needs: [build-and-push, deploy-staging]
    if: github.event.inputs.environment == 'production' || github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    environment:
      name: production
      url: https://api.example.com

    steps:
      - uses: actions/checkout@v4
      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID_PROD }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID_PROD }}

      - name: Set AKS context
        uses: azure/aks-set-context@v4
        with:
          resource-group: ${{ secrets.AKS_RESOURCE_GROUP_PROD }}
          cluster-name: ${{ secrets.AKS_CLUSTER_NAME_PROD }}

      - name: Deploy with canary
        run: |
          # Update canary deployment first (10% traffic)
          kubectl set image deployment/order-service-canary \
            order-service=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }} \
            -n order-service
          kubectl rollout status deployment/order-service-canary -n order-service --timeout=300s

          # Monitor for 5 minutes
          echo "Monitoring canary for 5 minutes..."
          sleep 300

          # Promote to full deployment
          kubectl set image deployment/order-service \
            order-service=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }} \
            -n order-service
          kubectl rollout status deployment/order-service -n order-service --timeout=300s
```

## Reusable Workflow (shared across services)

<!-- sample: tests/Workflows/reusable-build.yml -->
```yaml
# .github/workflows/dotnet-service.yml (reusable)
name: .NET Service CI

on:
  workflow_call:
    inputs:
      service-name:
        required: true
        type: string
      dockerfile-path:
        required: true
        type: string
      dotnet-version:
        required: false
        type: string
        default: '10.0.x'

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ inputs.dotnet-version }}
      - run: dotnet build -warnaserror
      - run: dotnet test --filter "Category!=Integration"
```

The calling workflow in each service:

<!-- sample: tests/Workflows/reusable-build-caller.yml -->
```yaml
# .github/workflows/ci.yml (in the service)
name: Order Service CI
on: [push, pull_request]
jobs:
  ci:
    uses: ./.github/workflows/dotnet-service.yml
    with:
      service-name: order-service
      dockerfile-path: src/OrderService.Api/Dockerfile
```
