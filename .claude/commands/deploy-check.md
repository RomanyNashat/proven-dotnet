---
name: deploy-check
description: Validate Kubernetes manifests, CI/CD pipeline configuration, and deployment readiness. Delegates to devops-engineer agent.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

Delegate to the **devops-engineer** agent.

## Deployment Readiness Check

### 1. Kubernetes Manifests
- Validate YAML syntax: `kubectl apply --dry-run=client -f k8s/`
- Check resource requests AND limits are set on every container
- Verify liveness, readiness, and startup probes are configured
- Verify PodDisruptionBudget exists for HA services
- Check NetworkPolicy restricts ingress/egress (zero-trust)
- Verify secrets come from CSI driver or sealed secrets (not plaintext K8s Secrets)
- Reference `skills/kubernetes-dotnet/`

### 2. CI/CD Pipeline
- Verify pipeline runs: build → test → Docker build → deploy
- Check NuGet cache is configured for faster builds
- Verify OIDC auth is used (no stored credentials)
- Check Docker BuildKit cache is enabled
- Verify environment-specific deployment (staging → production)

### 3. Health Checks
- `/health/live` exists (liveness — process alive)
- `/health/ready` exists (readiness — dependencies up)
- Health checks match probe configuration in K8s manifests

### 4. Rollback Plan
- Verify rollback command is documented
- Check previous image tag is accessible for quick rollback
- Verify database migrations are backward-compatible (N-1 support)
